using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World.Transitions;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Composes authenticated portal preparation, one durable CAS, exact receipt verification,
/// and partition handoff for characters WITHOUT active sessions. This is deliberately
/// not registered with any gameplay ingress and provides no crash-safe writer lease.
/// </summary>
internal static class WorldUnboundPortalTransferCoordinator
{
    private static readonly TimeSpan s_databaseOperationTimeout = TimeSpan.FromSeconds(15);

    internal static async ValueTask<WorldCharacterOwnership?> TryExecuteAsync(
        WorldRuntime runtime, MapTransitionCatalog transitions, CharacterLoginProfile authenticatedProfile,
        WorldCharacterOwnership ownership, ICharacterLocationObservationStore observations,
        ICharacterLocationWriteStore writes, ushort portalX, ushort portalY,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(authenticatedProfile);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(writes);
        cancellationToken.ThrowIfCancellationRequested();

        using WorldPortalPersistedLocationReservation? prepared = await
            WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions, authenticatedProfile,
                ownership, observations, portalX, portalY, cancellationToken).ConfigureAwait(false);
        if (prepared is null)
        {
            return null;
        }

        // This gate is the last cancellable boundary. After it is installed, a timeout,
        // cancellation, conflict, or unexpected result is terminal, not a safe retry.
        WorldDurableTransferSeal? seal = runtime.TrySealUnboundPortalTransfer(prepared.Reservation, cancellationToken);
        if (seal is null)
        {
            return null;
        }

        try
        {
            CharacterLocation destination = new(prepared.DestinationMapId,
                prepared.DestinationX, prepared.DestinationY);
            CharacterLocationChange change = new(authenticatedProfile.Identity.AccountId,
                ownership.CharacterId, prepared.PersistedSource.Location, destination,
                prepared.PersistedSource.Revision, Guid.NewGuid());

            if (!runtime.IsRunning)
            {
                throw new InvalidOperationException("The world stopped after portal commit fencing.");
            }

            using CancellationTokenSource writeDeadline = new(s_databaseOperationTimeout);
            CharacterLocationWriteResult writeResult = await writes.TryCommitAsync(change, writeDeadline.Token)
                .AsTask().WaitAsync(writeDeadline.Token).ConfigureAwait(false);
            if (writeResult != CharacterLocationWriteResult.Committed)
            {
                throw new InvalidOperationException("The sealed portal write did not commit; source residency is no longer trustworthy.");
            }

            using CancellationTokenSource readDeadline = new(s_databaseOperationTimeout);
            PersistedCharacterLocation? observed = await observations.ObserveAsync(change.AccountId,
                    change.CharacterId, readDeadline.Token).AsTask().WaitAsync(readDeadline.Token)
                .ConfigureAwait(false);
            if (!CharacterLocationCommitEvidence.IsConfirmed(change, observed))
            {
                throw new InvalidOperationException("Exact durable portal commit evidence was not available.");
            }

            WorldCharacterOwnership? transferred = await runtime.TryRelocateSealedCharacterAsync(seal)
                .ConfigureAwait(false);
            if (transferred is null)
            {
                throw new InvalidOperationException("Confirmed durable portal write could not complete world handoff.");
            }

            return transferred;
        }
        catch (Exception exception)
        {
            // After sealing, no observation (including an apparently unchanged source)
            // can authorize reopening gameplay. A late DB command can still complete.
            try
            {
                await runtime.StopAsync().ConfigureAwait(false);
            }
            catch (Exception shutdownFailure)
            {
                throw new AggregateException("Portal commit failed and world shutdown also failed.",
                    exception, shutdownFailure);
            }
            throw;
        }
    }
}
