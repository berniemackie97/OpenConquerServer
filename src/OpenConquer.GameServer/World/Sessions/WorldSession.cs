using System.Diagnostics;
using System.Threading.Channels;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Chat;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Movement;
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
    private readonly WorldVisibilityService? _visibility;
    private readonly Channel<OutboundDelivery>? _visibilityMailbox;
    private readonly CancellationTokenSource _visibilityStopping = new();
    private Task? _visibilityWrites;
    private WorldWalkPacer _walkPacer = new();
    private readonly WorldJumpIngressLimiter _jumpIngressLimiter = new();
    private readonly WorldAvatarActionLimiter _avatarActionLimiter = new();
    private readonly WorldChatRateLimiter _chatRateLimiter = new();
    private WorldPositionReconciler _positionReconciler;
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
        WorldCharacterOwnership ownership, WorldVisibilityService? visibility = null,
        WorldConnectedPortalTransferCoordinator? portals = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));

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
        _positionReconciler = new WorldPositionReconciler(bootstrap.Profile.Location.X, bootstrap.Profile.Location.Y);
    }

    public ExistingCharacterBootstrapCompleteConnection Bootstrap { get; }
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

        _positionReconciler = new WorldPositionReconciler(persisted.Location.X, persisted.Location.Y);
        _walkPacer = new WorldWalkPacer();
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
    internal void ProcessTradeQuit(GameTradeQuitRequest request)
    {
        ThrowIfInactive();
        if (request.EntityId != Ownership.CharacterId)
        {
            throw new InvalidDataException("Trade quit does not belong to the authenticated character.");
        }
    }

    /// <summary>
    /// Applies one 8-direction ground step. The packet is echoed back only after the partition has committed the
    /// new position.
    /// </summary>
    internal async ValueTask ProcessWalkAsync(GameWalkPacket walk, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();

        if (walk.EntityId != Ownership.CharacterId)
        {
            throw new InvalidDataException("A walk command does not match the authenticated character.");
        }

        if (!_walkPacer.TryAccept(walk.MovementTick, Stopwatch.GetTimestamp(), walk.IsTwentyFourDirection))
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldVisibilityService? visibility = _visibility;
        Action<WorldCharacterSnapshot>? publish = visibility is null ? null : snapshot =>
        {
            // Publish committed movement first: a full sender mailbox may
            // start teardown, but healthy observers must not lose this movement.
            // Both deliveries are scheduled in this partition turn.
            visibility.Moved(this, snapshot, walk);
            QueueVisibilityPacket(walk);
        };
        WorldCharacterSnapshot? committed = await _runtime.TryWalkCharacterAsync(Ownership, walk.DirectionIndex,
            walk.CommandSelector, publish, linked.Token).ConfigureAwait(false);

        if (committed is not null)
        {
            _positionReconciler.RecordAuthorized(committed.Value.X, committed.Value.Y);
            if (visibility is null)
            {
                await WriteAsync(walk, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles 5517 request 145. The client never gets an action-145 echo.
    /// </summary>
    internal async ValueTask ProcessGhostTransitionAsync(GameGhostTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        if (request.EntityId != Ownership.CharacterId)
        {
            throw new InvalidDataException("Ghost transition does not belong to the authenticated character.");
        }

        // Validate the appearance before a partition command can commit the status.
        uint look = WorldGhostAppearance.FromBaseLook(Profile.Appearance.Composite);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldVisibilityService? visibility = _visibility;
        Action<WorldCharacterSnapshot>? publish = visibility is null ? null : ghost =>
        {
            visibility.Ghosted(this, ghost, look);
            // The local client needs status before look, as in the older TQ server.
            QueueVisibilityPacket(new GameUserAttributePacket(Ownership.CharacterId,
                GameUserAttributePacket.PrimaryStatusAttribute, ghost.Status.Low, ghost.Status.High));
            QueueVisibilityPacket(new GameUserAttributePacket(Ownership.CharacterId,
                GameUserAttributePacket.LookAttribute, look));
        };
        WorldCharacterSnapshot? transitioned = await _runtime.TryEnterGhostAsync(Ownership,
            Stopwatch.GetTimestamp(), publish, linked.Token).ConfigureAwait(false);
        if (transitioned is WorldCharacterSnapshot snapshot && visibility is null)
        {
            await WriteAsync(new GameUserAttributePacket(Ownership.CharacterId,
                GameUserAttributePacket.PrimaryStatusAttribute, snapshot.Status.Low, snapshot.Status.High),
                cancellationToken).ConfigureAwait(false);
            await WriteAsync(new GameUserAttributePacket(Ownership.CharacterId,
                GameUserAttributePacket.LookAttribute, look), cancellationToken).ConfigureAwait(false);
        }
        // Too early, already ghosted, or no longer dead: idempotently ignore.
    }

    /// <summary>
    /// Applies a jump to an absolute tile once the partition accepts it.
    /// </summary>
    internal async ValueTask ProcessJumpAsync(GameJumpAction jump, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        if (jump.EntityId != Ownership.CharacterId || jump.Action.Action != GameJumpAction.JumpAction ||
            jump.Action.StringCount != 0)
        {
            throw new InvalidDataException("Jump intent does not belong to the authenticated character.");
        }

        // Subtype 137 with +36 = 1 is a mounted charge, not a ground jump, and can't be allowed or echoed until riding
        // and vigor exist. Unknown nonzero variants are refused and corrected the same way. The ingress limit still
        // applies on maps where jumping isn't allowed.
        if (!_jumpIngressLimiter.TryAccept(Stopwatch.GetTimestamp()) ||
            !jump.IsGroundJump || (Map.Flags & 0x0200_0000UL) != 0)
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? before = await _runtime.ReadCharacterAsync(Ownership, linked.Token).ConfigureAwait(false);
        if (before is null)
        {
            throw new InvalidDataException("Jump intent has no active authoritative world owner.");
        }
        int distance = MapDistance.RoundedTileDistance(before.Value.X, before.Value.Y, jump.TargetX, jump.TargetY);
        if (distance is < 1 or > 16 || !_walkPacer.TryAcceptJump(jump.Timestamp, Stopwatch.GetTimestamp(), distance))
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        GameActionPacket acknowledgement = jump.CreateAcknowledgement();
        WorldVisibilityService? visibility = _visibility;
        Action<WorldCharacterSnapshot>? publish = visibility is null ? null : snapshot =>
        {
            visibility.Jumped(this, snapshot, acknowledgement);
            QueueVisibilityPacket(acknowledgement);
        };
        WorldCharacterSnapshot? committed = await _runtime.TryJumpCharacterAsync(Ownership,
            jump.TargetX, jump.TargetY, publish, linked.Token).ConfigureAwait(false);
        if (committed is not null)
        {
            _positionReconciler.RecordAuthorized(committed.Value.X, committed.Value.Y);
            if (visibility is null)
            {
                await WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes only proven native world-chat channels. Unknown valid channels are
    /// deliberately not relayed as unscoped chat or used to forge control messages.
    /// </summary>
    internal async ValueTask ProcessChatAsync(GameInboundChat request, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();

        // Charge all well-formed inbound chat traffic, including unsupported
        // channels and unaddressed whispers, before selecting a delivery path.
        // One serialized reader applies backpressure without an auxiliary queue.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        while (!_chatRateLimiter.TryAccept(Stopwatch.GetTimestamp(), out TimeSpan retryAfter))
        {
            await Task.Delay(retryAfter, linked.Token).ConfigureAwait(false);
            ThrowIfInactive();
        }

        ThrowIfInactive();
        if (!request.IsSupported)
        {
            return;
        }

        if (request.IsWhisper)
        {
            // The ordinary client text box uses ALL even on a selected tab.
            // Without an explicit target there is no safe whisper recipient.
            if (request.Recipient == GameInboundChat.BroadcastRecipient)
            {
                return;
            }
        }
        else if (request.Recipient != GameInboundChat.BroadcastRecipient)
        {
            throw new InvalidDataException("Local speech and role words must target ALL.");
        }

        GameChatPacket? acknowledgement = _visibility?.PublishChat(this, request);
        if (acknowledgement is not null)
        {
            await SendGameplayResponseAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a facing or pose change without moving the character. Observers get it through the usual
    /// visibility queue.
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
        // Authorize and publish within one map-partition turn. A corpse cannot
        // spoof a standing pose or facing, including in the late-observer cache.
        GameActionPacket acknowledgement = request.CreateAcknowledgement();
        bool authorized = await _runtime.TryPublishAvatarActionAsync(Ownership,
            _ => _visibility?.AvatarAction(this, request, acknowledgement), linked.Token).ConfigureAwait(false);
        if (!authorized)
        {
            return;
        }

        ThrowIfInactive();
        await SendGameplayResponseAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
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

        await SendGameplayResponseAsync(CreatePositionCorrection(snapshot.Value), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The native 408 report has no response and confers no gameplay authority.</summary>
    internal void ProcessClientStateApplied(GameAction notification)
    {
        ThrowIfInactive();
        if (notification.Action != GameAction.ClientStateAppliedAction ||
            notification.EntityId != 0 || notification.ParameterPair != 0 ||
            notification.ActionParameter != 0 || notification.Timestamp != 0 ||
            notification.Direction != 0 || notification.PositionX != 0 ||
            notification.PositionY != 0 || notification.Data1 != 0 ||
            notification.Data2 != 0 || notification.Flag != 0 || notification.StringCount != 0)
        {
            throw new InvalidDataException("The 5517 client-state-applied report is malformed.");
        }
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

    /// <summary>
    /// Answers a refused walk or jump with the server's tile so the client snaps back. The per-session limit caps
    /// snapshot reads and corrections under packet spam. Unprompted position reports are handled separately.
    /// </summary>
    private async ValueTask CorrectRejectedMovementAsync(CancellationToken cancellationToken)
    {
        if (!_positionReconciler.TryReserveRejectedMovementCorrection(Stopwatch.GetTimestamp()))
        {
            // The reader closes protocol-abusive sessions: never leave a connected
            // client on a rejected, locally predicted tile without a correction.
            throw new InvalidDataException(
                "Movement denial correction budget exhausted.");
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? authoritative = await _runtime.ReadCharacterAsync(Ownership, linked.Token)
            .ConfigureAwait(false);
        if (authoritative is null)
        {
            throw new InvalidDataException("Rejected movement no longer has authoritative world ownership.");
        }

        _positionReconciler.RecordAuthorized(authoritative.Value.X, authoritative.Value.Y);
        await SendGameplayResponseAsync(CreatePositionCorrection(authoritative.Value), cancellationToken)
            .ConfigureAwait(false);
    }

    private GameActionPacket CreatePositionCorrection(WorldCharacterSnapshot position) =>
        new(Ownership.CharacterId, parameterPair: 0, actionParameter: 0,
            timestamp: unchecked((uint)Environment.TickCount64), action: 108, direction: 0,
            positionX: position.X, positionY: position.Y, data1: 0, data2: 0, flag: 0);

    /// <summary>
    /// Keep local gameplay responses in the same per-session FIFO as peer spawns and
    /// movement. Sessions without visibility preserve their original direct send.
    /// </summary>
    private ValueTask SendGameplayResponseAsync(IPacket packet, CancellationToken cancellationToken)
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

    private sealed record WorldLocationState(WorldCharacterOwnership Ownership, WorldMapContent Map,
        PersistedCharacterLocation? Persisted);

    private readonly record struct OutboundDelivery(IPacket First, IPacket? Second, IPacket[]? Snapshot = null);
}
