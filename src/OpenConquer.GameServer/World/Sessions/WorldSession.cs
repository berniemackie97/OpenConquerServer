using System.Diagnostics;
using System.Threading.Channels;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Movement;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.World.Sessions;

/// <summary>
/// Owns an authenticated connection, its completed login snapshots, and one authoritative world residency.
/// Revocation and world shutdown initiate the same supervised, idempotent teardown.
/// </summary>
/// <remarks>
/// Presence is revoked only when the account logs in again, here or in another realm. The session then sends
/// the native kick notice (1115) before closing, as the 5517 client expects when it is logged out by another login.
/// </remarks>
internal sealed class WorldSession : IAsyncDisposable
{
    private static readonly GameKickoutNoticePacket s_otherLoginNotice = new(GameKickoutReason.OtherLogin, "Your account was logged in from another location.");
    private static readonly TimeSpan s_noticeTimeout = TimeSpan.FromSeconds(2);

    private readonly ExistingCharacterGameConnection _connection;
    private readonly WorldRuntime _runtime;
    private readonly WorldVisibilityService? _visibility;
    private readonly Channel<IPacket>? _visibilityMailbox;
    private readonly CancellationTokenSource _visibilityStopping = new();
    private Task? _visibilityWrites;
    private readonly WorldWalkPacer _walkPacer = new();
    private readonly WorldAvatarActionLimiter _avatarActionLimiter = new();
    private readonly WorldPositionReconciler _positionReconciler;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CancellationTokenRegistration _presenceRegistration;
    private CancellationTokenRegistration _runtimeRegistration;
    private int _disposalStarted;
    private int _supersededByLogin;

    internal WorldSession(WorldRuntime runtime, ExistingCharacterGameConnection connection, ExistingCharacterBootstrapCompleteConnection bootstrap,
        WorldCharacterOwnership ownership, WorldVisibilityService? visibility = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));

        if (ownership.CharacterId != bootstrap.Profile.Identity.CharacterId || ownership.MapId != bootstrap.Map.MapId || ownership.RuntimeId == Guid.Empty)
        {
            throw new ArgumentException("World ownership does not match the authenticated bootstrap character and map.", nameof(ownership));
        }

        Ownership = ownership;
        _visibility = visibility;
        if (visibility is not null)
        {
            _visibilityMailbox = Channel.CreateBounded<IPacket>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }
        _positionReconciler = new WorldPositionReconciler(bootstrap.Profile.Location.X, bootstrap.Profile.Location.Y);
    }

    public ExistingCharacterBootstrapCompleteConnection Bootstrap { get; }
    public CharacterLoginProfile Profile => Bootstrap.Profile;
    public WorldMapContent Map => Bootstrap.Map;
    public WorldCharacterOwnership Ownership { get; }
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

    /// <summary>Only the world visibility index may enqueue peer packets.</summary>
    internal void QueueVisibilityPacket(IPacket packet)
    {
        if (IsStopping || _visibilityMailbox is null)
        {
            return;
        }
        if (!_visibilityMailbox.Writer.TryWrite(packet))
        {
            // A slow peer cannot retain unbounded visibility history or stall other players.
            BeginDispose();
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
            await foreach (IPacket packet in _visibilityMailbox.Reader.ReadAllAsync(_visibilityStopping.Token).ConfigureAwait(false))
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
                    // Do not call WorldSession.WriteAsync here: its fail-and-await-cleanup path
                    // would await this pump and deadlock session disposal.
                    await _connection.WriteAsync(packet, linked.Token).ConfigureAwait(false);
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
    /// Applies exactly one authenticated eight-direction ground step. The packet is echoed
    /// only after the authoritative partition commits the new position.
    /// </summary>
    internal async ValueTask ProcessWalkAsync(GameWalkPacket walk, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();

        if (walk.EntityId != Ownership.CharacterId || walk.DirectionIndex >= 8 || walk.CommandSelector == GameWalkPacket.TwentyFourDirectionSelector)
        {
            throw new InvalidDataException("A walk command does not match the authenticated character or supported native direction mode.");
        }

        if (!_walkPacer.TryAccept(walk.MovementTick, Stopwatch.GetTimestamp()))
        {
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? committed = await _runtime.TryWalkCharacterAsync(Ownership, walk.DirectionIndex, linked.Token).ConfigureAwait(false);

        if (committed is not null)
        {
            _positionReconciler.RecordAuthorized(committed.Value.X, committed.Value.Y);
            await WriteAsync(walk, cancellationToken).ConfigureAwait(false);
            _visibility?.Moved(this, committed.Value, walk);
        }
    }

    /// <summary>
    /// Applies a verified 5517 facing or pose request without changing world position.
    /// Peer delivery uses the existing bounded visibility mailbox.
    /// </summary>
    internal async ValueTask ProcessAvatarActionAsync(GameAvatarAction request, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        if (request.EntityId != Ownership.CharacterId ||
            (request.Kind == GameAvatarAction.DirectionAction && request.Direction >= 8) ||
            (request.Kind == GameAvatarAction.PoseAction &&
             (!GameAvatarAction.IsSupportedPose(request.Pose) || request.Direction >= 8)) ||
            request.Kind is not (GameAvatarAction.DirectionAction or GameAvatarAction.PoseAction))
        {
            throw new InvalidDataException("An avatar action is invalid or does not belong to the authenticated character.");
        }

        // Backpressure the serialized reader instead of losing the final pose/facing.
        // A single cancellation-aware wait preserves order without an auxiliary queue.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        while (!_avatarActionLimiter.TryAccept(Stopwatch.GetTimestamp(), out TimeSpan retryAfter))
        {
            await Task.Delay(retryAfter, linked.Token).ConfigureAwait(false);
            ThrowIfInactive();
        }
        if (await _runtime.ReadCharacterAsync(Ownership, linked.Token).ConfigureAwait(false) is null)
        {
            throw new InvalidDataException("An avatar action has no authoritative world owner.");
        }

        ThrowIfInactive();
        GameActionPacket acknowledgement = request.CreateAcknowledgement();
        _visibility?.AvatarAction(this, request, acknowledgement);
        await WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles unsolicited native position reports without trusting their coordinates.
    /// Only a repeated mismatch can elicit a native 108 correction; reports are never echoed.
    /// </summary>
    internal async ValueTask ProcessPositionReportAsync(GamePositionReport report, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();

        if (report.EntityId != Ownership.CharacterId || report.Action is not
            (GamePositionReport.IdleReportAction or GamePositionReport.CorrectionReplyAction))
        {
            throw new InvalidDataException("A position report does not match the authenticated character or native action.");
        }

        if (!_positionReconciler.ShouldCorrect(report.X, report.Y, Stopwatch.GetTimestamp()))
        {
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? snapshot = await _runtime.ReadCharacterAsync(Ownership, linked.Token).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new InvalidDataException("The authenticated character no longer has authoritative world ownership.");
        }

        _positionReconciler.RecordAuthorized(snapshot.Value.X, snapshot.Value.Y);

        if (snapshot.Value.X == report.X && snapshot.Value.Y == report.Y)
        {
            return;
        }

        GameActionPacket correction = new(Ownership.CharacterId, parameterPair: 0, actionParameter: 0,
            timestamp: unchecked((uint)Environment.TickCount64), action: 108, direction: 0,
            positionX: snapshot.Value.X, positionY: snapshot.Value.Y, data1: 0, data2: 0, flag: 0);
        await WriteAsync(correction, cancellationToken).ConfigureAwait(false);
    }

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
            throw new AggregateException("World-session I/O failed and cleanup also failed.", operationFailure, cleanupFailure);
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
            await SendOtherLoginNoticeAsync().ConfigureAwait(false);
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
            else if (failures.Count == 1)
            {
                _completion.TrySetException(failures[0]);
            }
            else
            {
                _completion.TrySetException(new AggregateException("World session connection and ownership cleanup encountered multiple failures.", failures));
            }
        }
        finally
        {
            _runtime.UntrackSession(Ownership, _completion.Task);
        }
    }
}
