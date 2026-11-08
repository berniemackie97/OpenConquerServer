using System.Threading.Channels;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Time;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.World.Sessions;

/// <summary>
/// A logged-in character in the world: the connection, its login state and its place in the world runtime.
/// Revocation and world shutdown go through the same teardown, which only runs once.
/// </summary>
/// <remarks>
/// Presence is revoked only when the account logs in again, here or in another realm. The session then sends
/// the kick notice (1115) before closing, as the 5517 client expects when another login replaces it.
/// </remarks>
internal sealed class WorldSession : IAsyncDisposable
{
    private static readonly GameKickoutNoticePacket s_otherLoginNotice = new(GameKickoutReason.OtherLogin, "Your account was logged in from another location.");
    private static readonly TimeSpan s_noticeTimeout = TimeSpan.FromSeconds(2);

    private readonly ExistingCharacterGameConnection _connection;
    private readonly WorldRuntime _runtime;
    private readonly WorldSessionGameplay _gameplay;
    private readonly WorldVisibilityService? _visibility;
    private readonly Channel<OutboundDelivery>? _visibilityMailbox;
    private readonly CancellationTokenSource _visibilityStopping = new();
    private Task? _visibilityWrites;
    private WorldLocationState _location;
    private readonly WorldConnectedPortalTransferCoordinator? _portals;
    private TaskCompletionSource? _portalOperation;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CancellationTokenRegistration _presenceRegistration;
    private CancellationTokenRegistration _runtimeRegistration;
    private int _disposalStarted;
    private int _supersededByLogin;

    internal WorldSession(WorldRuntime runtime, ExistingCharacterGameConnection connection, ExistingCharacterBootstrapCompleteConnection bootstrap,
        WorldCharacterOwnership ownership, IGameTickSource tickSource, WorldVisibilityService? visibility = null,
        WorldConnectedPortalTransferCoordinator? portals = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        ArgumentNullException.ThrowIfNull(tickSource);

        if (ownership.CharacterId != bootstrap.Profile.Identity.CharacterId || ownership.MapId != bootstrap.Map.MapId || ownership.RuntimeId == Guid.Empty)
        {
            throw new ArgumentException("World ownership does not match the authenticated bootstrap character and map.", nameof(ownership));
        }

        _location = new WorldLocationState(ownership, bootstrap.Map, connection.LocationAuthority?.Snapshot);
        _visibility = visibility;
        _portals = portals;
        if (visibility is not null)
        {
            _visibilityMailbox = Channel.CreateBounded<OutboundDelivery>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }
        _gameplay = new WorldSessionGameplay(this, runtime, visibility, tickSource, bootstrap.Profile.Location.X, bootstrap.Profile.Location.Y);
    }

    public ExistingCharacterBootstrapCompleteConnection Bootstrap
    {
        get;
    }
    public CharacterLoginProfile Profile => Bootstrap.Profile;
    public CharacterLocationWriteAuthority? LocationAuthority => _connection.LocationAuthority;
    public WorldMapContent Map => Volatile.Read(ref _location).Map;
    public WorldCharacterOwnership Ownership => Volatile.Read(ref _location).Ownership;
    internal PersistedCharacterLocation? PersistedLocation => Volatile.Read(ref _location).Persisted;
    public Task Completion => _completion.Task;
    public bool IsStopping => Volatile.Read(ref _disposalStarted) != 0;

    internal void Activate()
    {
        CancellationTokenRegistration presence = _connection.RevocationToken.UnsafeRegister(static state => ((WorldSession)state!).OnPresenceRevoked(), this);
        CancellationTokenRegistration runtime;

        try
        {
            runtime = _runtime.StoppingToken.UnsafeRegister(static state => ((WorldSession)state!).BeginDispose(), this);
        }
        catch
        {
            presence.Dispose();
            throw;
        }

        bool disposeRegistrations;

        lock (_gate)
        {
            disposeRegistrations = _disposalStarted != 0;

            if (!disposeRegistrations)
            {
                _presenceRegistration = presence;
                _runtimeRegistration = runtime;
            }
        }

        if (disposeRegistrations)
        {
            presence.Dispose();
            runtime.Dispose();
        }

        if (_visibilityMailbox is not null)
        {
            _visibilityWrites = Task.Run(PumpVisibilityAsync);
        }

        if (_connection.IsRevoked)
        {
            OnPresenceRevoked();
        }
        else if (!_runtime.IsRunning)
        {
            BeginDispose();
        }
    }

    public void ThrowIfInactive()
    {
        ObjectDisposedException.ThrowIf(IsStopping, this);
        _connection.ThrowIfRevoked();

        if (!_runtime.IsRunning)
        {
            throw new InvalidOperationException("The authoritative world runtime is stopping.");
        }
    }

    /// <summary>Hydrates the local client's corpse status after login reset, before visibility entry.</summary>
    internal async ValueTask SynchronizeInitialStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        if (Profile.Vitals.Life != 0)
        {
            return;
        }

        WorldCharacterSnapshot? current = await _runtime.ReadCharacterAsync(Ownership, cancellationToken).ConfigureAwait(false);
        if (current is not WorldCharacterSnapshot snapshot || snapshot.IsAlive)
        {
            throw new InvalidDataException("The admitted corpse status does not match authoritative world residency.");
        }

        GameUserAttributePacket packet = new(Ownership.CharacterId, GameUserAttributePacket.PrimaryStatusAttribute,
            snapshot.Status.Low, snapshot.Status.High);
        if (_visibilityMailbox is not null)
        {
            QueueVisibilityPacket(packet);
        }
        else
        {
            await WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void EnterVisibility()
    {
        ThrowIfInactive();
        _visibility?.Enter(this);
    }

    internal void QueryVisibleEntity(uint subjectId)
    {
        ThrowIfInactive();
        _visibility?.Query(this, subjectId);
    }

    /// <summary>
    /// Queues a packet for this session, in order. Movement and replication from partition turns land here
    /// without waiting on the socket.
    /// </summary>
    internal void QueueVisibilityPacket(IPacket packet)
    {
        if (IsStopping || _visibilityMailbox is null)
        {
            return;
        }
        if (!_visibilityMailbox.Writer.TryWrite(new OutboundDelivery(packet, null)))
        {
            // A slow peer cannot retain unbounded visibility history or stall other players.
            BeginDispose();
        }
    }

    /// <summary>
    /// Called under the source/destination visibility cutover locks; never exposed as an
    /// independent network operation where old-map fanout could overtake arrival.
    /// </summary>
    internal void QueueVisibilitySnapshot(IPacket[] packets)
    {
        if (packets.Length == 0 || IsStopping || _visibilityMailbox is null)
        {
            return;
        }
        if (!_visibilityMailbox.Writer.TryWrite(new OutboundDelivery(packets[0], null, packets)))
        {
            BeginDispose();
        }
    }

    internal void QueuePortalArrival(IPacket arrival, IPacket mapInfo)
    {
        if (_visibilityMailbox is null)
        {
            throw new InvalidOperationException("Connected portal arrival requires an ordered world mailbox.");
        }
        if (!_visibilityMailbox.Writer.TryWrite(new OutboundDelivery(arrival, mapInfo)))
        {
            BeginDispose();
        }
    }

    /// <summary>Called only under WorldRuntime's final generation handoff gate.</summary>
    internal void RebindPortalWorld(WorldCharacterOwnership oldOwnership, WorldCharacterOwnership next,
        WorldMapContent target, PersistedCharacterLocation persisted)
    {
        WorldLocationState previous = Volatile.Read(ref _location);
        if (previous.Ownership != oldOwnership || next.CharacterId != oldOwnership.CharacterId ||
            next.MapId != target.MapId || persisted.Location.MapId != next.MapId)
        {
            throw new InvalidOperationException("Connected portal rebind lost its authoritative world generation.");
        }

        _gameplay.RebindLocation(persisted.Location.X, persisted.Location.Y);
        Volatile.Write(ref _location, new WorldLocationState(next, target, persisted));
    }

    internal async ValueTask ProcessPortalAsync(GamePortalRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EntityId != Ownership.CharacterId || _portals is null)
        {
            throw new InvalidDataException("Portal request has no matching authenticated world session.");
        }

        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposalStarted != 0 || _portalOperation is not null)
            {
                throw new InvalidDataException("A portal operation cannot overlap session disposal or another portal.");
            }
            _portalOperation = pending;
        }

        try
        {
            if (!await _portals.TryTransferAsync(this, request, cancellationToken).ConfigureAwait(false))
            {
                // 5517 waits indefinitely after 85. A refused transition must close the connection.
                throw new InvalidDataException("The requested portal is not authorized or available.");
            }
        }
        finally
        {
            lock (_gate)
            {
                _portalOperation = null;
            }
            pending.TrySetResult();
        }
    }

    private async Task PumpVisibilityAsync()
    {
        if (_visibilityMailbox is null)
        {
            return;
        }
        try
        {
            await foreach (OutboundDelivery delivery in _visibilityMailbox.Reader.ReadAllAsync(_visibilityStopping.Token).ConfigureAwait(false))
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _visibilityStopping.Token, _runtime.StoppingToken);
                await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (IsStopping)
                    {
                        return;
                    }
                    // Do not call WorldSession.WriteAsync here: that method awaits teardown.
                    if (delivery.Snapshot is IPacket[] snapshot)
                    {
                        foreach (IPacket packet in snapshot)
                        {
                            await _connection.WriteAsync(packet, linked.Token).ConfigureAwait(false);
                        }
                    }
                    else if (delivery.Second is IPacket second)
                    {
                        await _connection.WriteBatchAsync(delivery.First, second, linked.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await _connection.WriteAsync(delivery.First, linked.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _writeGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (_visibilityStopping.IsCancellationRequested || _runtime.StoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Delivery failure belongs to the recipient; never fault the sender or map partition.
            try
            {
                _visibility?.ReportPeerWriteFailure(Ownership.CharacterId, exception);
            }
            finally
            {
                BeginDispose();
            }
        }
    }

    /// <summary>
    /// Accepts the native 1056/2 trade-quit notification. Trading is not implemented:
    /// there is no exchange to release, response to send, or inventory to mutate.
    /// </summary>
    internal void ProcessTradeQuit(GameTradeQuitRequest request) => _gameplay.ProcessTradeQuit(request);

    /// <summary>
    /// Applies one 8-direction ground step. The packet is echoed back only after the partition has committed the
    /// new position.
    /// </summary>
    internal ValueTask ProcessWalkAsync(GameWalkPacket walk, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessWalkAsync(walk, cancellationToken);

    /// <summary>
    /// Handles 5517 request 145. The client never gets an action-145 echo.
    /// </summary>
    internal ValueTask ProcessGhostTransitionAsync(GameGhostTransitionRequest request, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessGhostTransitionAsync(request, cancellationToken);

    /// <summary>
    /// Applies a jump to an absolute tile once the partition accepts it.
    /// </summary>
    internal ValueTask ProcessJumpAsync(GameJumpAction jump, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessJumpAsync(jump, cancellationToken);

    /// <summary>
    /// Processes only proven native world-chat channels. Unknown valid channels are
    /// deliberately not relayed as unscoped chat or used to forge control messages.
    /// </summary>
    internal ValueTask ProcessChatAsync(GameInboundChat request, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessChatAsync(request, cancellationToken);

    /// <summary>
    /// Applies a facing or pose change without moving the character. Observers get it through the usual
    /// visibility queue.
    /// </summary>
    internal ValueTask ProcessAvatarActionAsync(GameAvatarAction request, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessAvatarActionAsync(request, cancellationToken);

    /// <summary>
    /// Reconciles unsolicited native position reports without trusting their coordinates.
    /// Only a repeated mismatch can elicit a native 108 correction; reports are never echoed.
    /// </summary>
    internal ValueTask ProcessPositionReportAsync(GamePositionReport report, CancellationToken cancellationToken = default) =>
        _gameplay.ProcessPositionReportAsync(report, cancellationToken);

    /// <summary>The native 408 report has no response and confers no gameplay authority.</summary>
    internal void ProcessClientStateApplied(GameAction notification) => _gameplay.ProcessClientStateApplied(notification);

    public async ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);

        GameInboundFrame? frame;

        try
        {
            frame = await _connection.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception operationFailure)
        {
            await FailConnectionAsync(operationFailure).ConfigureAwait(false);
            throw;
        }

        if (frame is null)
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        return frame;
    }

    /// <summary>
    /// Keep local gameplay responses in the same per-session FIFO as peer spawns and
    /// movement. Sessions without visibility preserve their original direct send.
    /// </summary>
    internal ValueTask SendGameplayResponseAsync(IPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfInactive();
        if (_visibilityMailbox is null)
        {
            return WriteAsync(packet, cancellationToken);
        }
        QueueVisibilityPacket(packet);
        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ThrowIfInactive();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);

        await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            await _connection.WriteAsync(packet, linked.Token).ConfigureAwait(false);
        }
        catch (Exception operationFailure)
        {
            _writeGate.Release();
            await FailConnectionAsync(operationFailure).ConfigureAwait(false);
            throw;
        }

        _writeGate.Release();
    }

    public ValueTask DisposeAsync()
    {
        BeginDispose();
        return new ValueTask(_completion.Task);
    }

    // Best effort: the client is told why it was disconnected, but a connection that cannot take one more frame
    // within the time limit is closed without it.
    private async Task SendOtherLoginNoticeAsync()
    {
        using CancellationTokenSource timeout = new(s_noticeTimeout);

        try
        {
            await _writeGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await _connection.WriteFarewellAsync(s_otherLoginNotice, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task FailConnectionAsync(Exception operationFailure)
    {
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            throw new GameLoginCleanupException("World-session I/O failed and cleanup also failed.", [operationFailure, cleanupFailure]);
        }
    }

    private void OnPresenceRevoked()
    {
        lock (_gate)
        {
            // The session's own teardown also releases its presence; only a revocation that arrives first
            // means that another login took the account over.
            if (_disposalStarted == 0)
            {
                Volatile.Write(ref _supersededByLogin, 1);
            }
        }

        BeginDispose();
    }

    private void BeginDispose()
    {
        lock (_gate)
        {
            if (_disposalStarted != 0)
            {
                return;
            }

            Volatile.Write(ref _disposalStarted, 1);
            _ = Task.Run(DisposeCoreAsync);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception> failures = [];
        Task? portal;
        lock (_gate)
        {
            portal = _portalOperation?.Task;
        }
        // Never release a source or destination generation while a durable transfer is unresolved.
        if (portal is not null)
        {
            await portal.ConfigureAwait(false);
        }

        try
        {
            _visibility?.Leave(this);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        _visibilityMailbox?.Writer.TryComplete();
        try
        {
            await _visibilityStopping.CancelAsync().ConfigureAwait(false);
            if (_visibilityWrites is not null)
            {
                await _visibilityWrites.ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            _visibilityStopping.Dispose();
        }

        if (Volatile.Read(ref _supersededByLogin) != 0)
        {
            try
            {
                await SendOtherLoginNoticeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _runtime.ReleaseCharacterAsync(Ownership).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);

            try
            {
                await _runtime.StopPartitionExecutionAsync().ConfigureAwait(false);
            }
            catch (Exception stopFailure)
            {
                failures.Add(stopFailure);
            }
        }

        try
        {
            CancellationTokenRegistration presence;
            CancellationTokenRegistration runtime;

            lock (_gate)
            {
                presence = _presenceRegistration;
                runtime = _runtimeRegistration;
                _presenceRegistration = default;
                _runtimeRegistration = default;
            }

            presence.Dispose();
            runtime.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            if (failures.Count == 0)
            {
                _completion.TrySetResult();
            }
            else
            {
                _completion.TrySetException(new GameLoginCleanupException(
                    "World session connection and ownership cleanup failed.", failures));
            }
        }
        finally
        {
            _runtime.UntrackSession(Ownership, _completion.Task);
        }
    }

    private sealed record WorldLocationState(WorldCharacterOwnership Ownership, WorldMapContent Map,
        PersistedCharacterLocation? Persisted);

    private readonly record struct OutboundDelivery(IPacket First, IPacket? Second, IPacket[]? Snapshot = null);
}
