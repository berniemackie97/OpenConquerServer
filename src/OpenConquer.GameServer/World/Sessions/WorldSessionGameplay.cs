using System.Diagnostics;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Chat;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Movement;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.World.Sessions;

/// <summary>
/// Applies an admitted session's gameplay requests and owns its pacing and position-reconciliation state.
/// Connection lifetime, ordered output and authoritative world binding remain on WorldSession.
/// </summary>
internal sealed class WorldSessionGameplay
{
    private readonly WorldSession _session;
    private readonly WorldRuntime _runtime;
    private readonly WorldVisibilityService? _visibility;
    private WorldWalkPacer _walkPacer = new();
    private readonly WorldJumpIngressLimiter _jumpIngressLimiter = new();
    private readonly WorldAvatarActionLimiter _avatarActionLimiter = new();
    private readonly WorldChatRateLimiter _chatRateLimiter = new();
    private WorldPositionReconciler _positionReconciler;

    public WorldSessionGameplay(WorldSession session, WorldRuntime runtime, WorldVisibilityService? visibility, ushort x, ushort y)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(runtime);
        _session = session;
        _runtime = runtime;
        _visibility = visibility;
        _positionReconciler = new WorldPositionReconciler(x, y);
    }

    // Called at the same point in the runtime's generation-handoff gate as before extraction.
    public void RebindLocation(ushort x, ushort y)
    {
        _positionReconciler = new WorldPositionReconciler(x, y);
        _walkPacer = new WorldWalkPacer();
    }

    /// <summary>
    /// Accepts the native 1056/2 trade-quit notification. Trading is not implemented:
    /// there is no exchange to release, response to send, or inventory to mutate.
    /// </summary>
    internal void ProcessTradeQuit(GameTradeQuitRequest request)
    {
        _session.ThrowIfInactive();
        if (request.EntityId != _session.Ownership.CharacterId)
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
        _session.ThrowIfInactive();

        if (walk.EntityId != _session.Ownership.CharacterId)
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
            visibility.Moved(_session, snapshot, walk);
            _session.QueueVisibilityPacket(walk);
        };
        WorldCharacterSnapshot? committed = await _runtime.TryWalkCharacterAsync(_session.Ownership, walk.DirectionIndex,
            walk.CommandSelector, publish, linked.Token).ConfigureAwait(false);

        if (committed is not null)
        {
            _positionReconciler.RecordAuthorized(committed.Value.X, committed.Value.Y);
            if (visibility is null)
            {
                await _session.WriteAsync(walk, cancellationToken).ConfigureAwait(false);
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
        _session.ThrowIfInactive();
        if (request.EntityId != _session.Ownership.CharacterId)
        {
            throw new InvalidDataException("Ghost transition does not belong to the authenticated character.");
        }

        // Validate the appearance before a partition command can commit the status.
        uint look = WorldGhostAppearance.FromBaseLook(_session.Profile.Appearance.Composite);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldVisibilityService? visibility = _visibility;
        Action<WorldCharacterSnapshot>? publish = visibility is null ? null : ghost =>
        {
            visibility.Ghosted(_session, ghost, look);
            // The local client needs status before look, as in the older TQ server.
            _session.QueueVisibilityPacket(new GameUserAttributePacket(_session.Ownership.CharacterId,
                GameUserAttributePacket.PrimaryStatusAttribute, ghost.Status.Low, ghost.Status.High));
            _session.QueueVisibilityPacket(new GameUserAttributePacket(_session.Ownership.CharacterId,
                GameUserAttributePacket.LookAttribute, look));
        };
        WorldCharacterSnapshot? transitioned = await _runtime.TryEnterGhostAsync(_session.Ownership,
            Stopwatch.GetTimestamp(), publish, linked.Token).ConfigureAwait(false);
        if (transitioned is WorldCharacterSnapshot snapshot && visibility is null)
        {
            await _session.WriteAsync(new GameUserAttributePacket(_session.Ownership.CharacterId,
                GameUserAttributePacket.PrimaryStatusAttribute, snapshot.Status.Low, snapshot.Status.High),
                cancellationToken).ConfigureAwait(false);
            await _session.WriteAsync(new GameUserAttributePacket(_session.Ownership.CharacterId,
                GameUserAttributePacket.LookAttribute, look), cancellationToken).ConfigureAwait(false);
        }
        // Too early, already ghosted, or no longer dead: idempotently ignore.
    }

    /// <summary>
    /// Applies a jump to an absolute tile once the partition accepts it.
    /// </summary>
    internal async ValueTask ProcessJumpAsync(GameJumpAction jump, CancellationToken cancellationToken = default)
    {
        _session.ThrowIfInactive();
        if (jump.EntityId != _session.Ownership.CharacterId || jump.Action.Action != GameJumpAction.JumpAction ||
            jump.Action.StringCount != 0)
        {
            throw new InvalidDataException("Jump intent does not belong to the authenticated character.");
        }

        // Subtype 137 with +36 = 1 is a mounted charge, not a ground jump, and can't be allowed or echoed until riding
        // and vigor exist. Unknown nonzero variants are refused and corrected the same way. The ingress limit still
        // applies on maps where jumping isn't allowed.
        if (!_jumpIngressLimiter.TryAccept(Stopwatch.GetTimestamp()) ||
            !jump.IsGroundJump || (_session.Map.Flags & 0x0200_0000UL) != 0)
        {
            await CorrectRejectedMovementAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? before = await _runtime.ReadCharacterAsync(_session.Ownership, linked.Token).ConfigureAwait(false);
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
            visibility.Jumped(_session, snapshot, acknowledgement);
            _session.QueueVisibilityPacket(acknowledgement);
        };
        WorldCharacterSnapshot? committed = await _runtime.TryJumpCharacterAsync(_session.Ownership,
            jump.TargetX, jump.TargetY, publish, linked.Token).ConfigureAwait(false);
        if (committed is not null)
        {
            _positionReconciler.RecordAuthorized(committed.Value.X, committed.Value.Y);
            if (visibility is null)
            {
                await _session.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
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
        _session.ThrowIfInactive();

        // Charge all well-formed inbound chat traffic, including unsupported
        // channels and unaddressed whispers, before selecting a delivery path.
        // One serialized reader applies backpressure without an auxiliary queue.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        while (!_chatRateLimiter.TryAccept(Stopwatch.GetTimestamp(), out TimeSpan retryAfter))
        {
            await Task.Delay(retryAfter, linked.Token).ConfigureAwait(false);
            _session.ThrowIfInactive();
        }

        _session.ThrowIfInactive();
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

        GameChatPacket? acknowledgement = _visibility?.PublishChat(_session, request);
        if (acknowledgement is not null)
        {
            await _session.SendGameplayResponseAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a facing or pose change without moving the character. Observers get it through the usual
    /// visibility queue.
    /// </summary>
    internal async ValueTask ProcessAvatarActionAsync(GameAvatarAction request, CancellationToken cancellationToken = default)
    {
        _session.ThrowIfInactive();
        if (request.EntityId != _session.Ownership.CharacterId ||
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
            _session.ThrowIfInactive();
        }
        // Authorize and publish within one map-partition turn. A corpse cannot
        // spoof a standing pose or facing, including in the late-observer cache.
        GameActionPacket acknowledgement = request.CreateAcknowledgement();
        bool authorized = await _runtime.TryPublishAvatarActionAsync(_session.Ownership,
            _ => _visibility?.AvatarAction(_session, request, acknowledgement), linked.Token).ConfigureAwait(false);
        if (!authorized)
        {
            return;
        }

        _session.ThrowIfInactive();
        await _session.SendGameplayResponseAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles unsolicited native position reports without trusting their coordinates.
    /// Only a repeated mismatch can elicit a native 108 correction; reports are never echoed.
    /// </summary>
    internal async ValueTask ProcessPositionReportAsync(GamePositionReport report, CancellationToken cancellationToken = default)
    {
        _session.ThrowIfInactive();

        if (report.EntityId != _session.Ownership.CharacterId || report.Action is not
            (GamePositionReport.IdleReportAction or GamePositionReport.CorrectionReplyAction))
        {
            throw new InvalidDataException("A position report does not match the authenticated character or native action.");
        }

        if (!_positionReconciler.ShouldCorrect(report.X, report.Y, Stopwatch.GetTimestamp()))
        {
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? snapshot = await _runtime.ReadCharacterAsync(_session.Ownership, linked.Token).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new InvalidDataException("The authenticated character no longer has authoritative world ownership.");
        }

        _positionReconciler.RecordAuthorized(snapshot.Value.X, snapshot.Value.Y);

        if (snapshot.Value.X == report.X && snapshot.Value.Y == report.Y)
        {
            return;
        }

        await _session.SendGameplayResponseAsync(CreatePositionCorrection(snapshot.Value), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The native 408 report has no response and confers no gameplay authority.</summary>
    internal void ProcessClientStateApplied(GameAction notification)
    {
        _session.ThrowIfInactive();
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
        WorldCharacterSnapshot? authoritative = await _runtime.ReadCharacterAsync(_session.Ownership, linked.Token)
            .ConfigureAwait(false);
        if (authoritative is null)
        {
            throw new InvalidDataException("Rejected movement no longer has authoritative world ownership.");
        }

        _positionReconciler.RecordAuthorized(authoritative.Value.X, authoritative.Value.Y);
        await _session.SendGameplayResponseAsync(CreatePositionCorrection(authoritative.Value), cancellationToken)
            .ConfigureAwait(false);
    }

    private GameActionPacket CreatePositionCorrection(WorldCharacterSnapshot position) =>
        new(_session.Ownership.CharacterId, parameterPair: 0, actionParameter: 0,
            timestamp: unchecked((uint)Environment.TickCount64), action: 108, direction: 0,
            positionX: position.X, positionY: position.Y, data1: 0, data2: 0, flag: 0);
}
