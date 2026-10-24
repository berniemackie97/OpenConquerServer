using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Durable, generation-fenced, connected cross-map transition. No network acknowledgment is
/// emitted until the exact write receipt and world owner handoff have both completed.
/// </summary>
internal sealed class WorldConnectedPortalTransferCoordinator(
    WorldRuntime runtime, MapTransitionCatalog transitions,
    ICharacterLocationObservationStore observations, ICharacterLocationWriteStore writes,
    WorldVisibilityService visibility)
{
    private static readonly TimeSpan s_observationDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_commitDeadline = TimeSpan.FromSeconds(15);

    private readonly WorldRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly MapTransitionCatalog _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
    private readonly ICharacterLocationObservationStore _observations = observations ?? throw new ArgumentNullException(nameof(observations));
    private readonly ICharacterLocationWriteStore _writes = writes ?? throw new ArgumentNullException(nameof(writes));
    private readonly WorldVisibilityService _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));

    internal async ValueTask<bool> TryTransferAsync(WorldSession session, GamePortalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        session.ThrowIfInactive();
        WorldCharacterOwnership source = session.Ownership;
        CharacterLocationWriteAuthority authority = session.LocationAuthority
            ?? throw new InvalidDataException("The connected character has no durable writer generation.");
        PersistedCharacterLocation current = session.PersistedLocation
            ?? throw new InvalidDataException("The connected character has no persisted source state.");

        uint accountId = session.Profile.Identity.AccountId;
        uint characterId = session.Profile.Identity.CharacterId;
        if (request.EntityId != characterId || source.CharacterId != characterId ||
            authority.AccountId != accountId || authority.CharacterId != characterId ||
            current.WriterToken != authority.Token || current.Location.MapId != source.MapId)
        {
            throw new InvalidDataException("The portal request has no valid authenticated writer owner.");
        }

        using WorldTransferReservation? reservation = await WorldPortalPreflight.TryReserveAsync(
            _runtime, _transitions, source, request.CandidateX, request.CandidateY, cancellationToken)
            .ConfigureAwait(false);
        if (reservation is null)
        {
            return false;
        }

        if (!_runtime.TryGetMap(reservation.DestinationMapId, out WorldMapContent? destination))
        {
            return false;
        }

        // A native 5517 hero arriving next to a portal will immediately send 85 again.
        foreach (MapTerrainExit exit in destination.BaseTerrain.Exits)
        {
            if (Math.Abs((int)reservation.X - exit.X) <= 1 &&
                Math.Abs((int)reservation.Y - exit.Y) <= 1)
            {
                return false;
            }
        }

        using (CancellationTokenSource observationTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            observationTimeout.CancelAfter(s_observationDeadline);
            PersistedCharacterLocation? saved = await _observations.ObserveAsync(accountId,
                characterId, observationTimeout.Token).AsTask().WaitAsync(observationTimeout.Token)
                .ConfigureAwait(false);
            observationTimeout.Token.ThrowIfCancellationRequested();
            if (saved is null || !Matches(current, saved) || !reservation.IsActive)
            {
                return false;
            }
        }

        session.ThrowIfInactive();
        WorldDurableTransferSeal? seal = _runtime.TrySealConnectedPortalTransfer(
            reservation, session.Completion, cancellationToken);
        if (seal is null)
        {
            return false;
        }

        bool worldTransferred = false;
        bool visibilityTransferred = false;
        try
        {
            CharacterLocation destinationLocation = new(reservation.DestinationMapId,
                reservation.X, reservation.Y);
            CharacterLocationChange change = new(accountId, characterId,
                current.Location, destinationLocation, current.Revision, Guid.NewGuid(), authority.Token);

            using (CancellationTokenSource writeTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                writeTimeout.CancelAfter(s_commitDeadline);
                CharacterLocationWriteResult result = await _writes.TryCommitAsync(change, writeTimeout.Token)
                    .AsTask().WaitAsync(writeTimeout.Token).ConfigureAwait(false);
                if (result != CharacterLocationWriteResult.Committed)
                {
                    throw new InvalidOperationException("The sealed portal location CAS was not committed.");
                }
            }

            PersistedCharacterLocation? receipt;
            using (CancellationTokenSource receiptTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                receiptTimeout.CancelAfter(s_observationDeadline);
                receipt = await _observations.ObserveAsync(accountId, characterId, receiptTimeout.Token)
                    .AsTask().WaitAsync(receiptTimeout.Token).ConfigureAwait(false);
                if (!CharacterLocationCommitEvidence.IsConfirmed(change, receipt))
                {
                    throw new InvalidOperationException("A sealed portal commit lacks exact durable receipt evidence.");
                }
            }

            PersistedCharacterLocation confirmed = receipt!;
            WorldCharacterOwnership? next = await _runtime.TryRelocateSealedConnectedCharacterAsync(
                seal, session.Completion,
                ownership => session.RebindPortalWorld(source, ownership, destination, confirmed))
                .ConfigureAwait(false);
            if (next is null)
            {
                throw new InvalidOperationException("The confirmed portal write failed world ownership handoff.");
            }
            worldTransferred = true;

            // Native 86 ends loading mode; 1110 must share its transport flush.
            // The map-create handler deletes previous roles, so destination spawns follow.
            GameMapInfoPacket mapInfo = new(destination.MapId, destination.MapDataId, destination.Flags);
            _visibility.Relocate(session, source, reservation.X, reservation.Y,
                destination.MapDataId, unchecked((uint)Environment.TickCount64), mapInfo);
            visibilityTransferred = true;
            return true;
        }
        catch
        {
            // An unknown or failed post-seal write can still commit later. Before a
            // complete visibility cutover, resuming any world session is unsafe.
            if (!worldTransferred || !visibilityTransferred)
            {
                await _runtime.StopPartitionExecutionAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private static bool Matches(PersistedCharacterLocation expected, PersistedCharacterLocation current) =>
        expected.Revision == current.Revision &&
        expected.LastOperationId == current.LastOperationId &&
        expected.WriterToken == current.WriterToken &&
        expected.Location.MapId == current.Location.MapId &&
        expected.Location.X == current.Location.X &&
        expected.Location.Y == current.Location.Y;
}
