using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World.Transitions;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Reversible portal preparation that binds a canonical world reservation to an identity-scoped
/// observation of the last persisted location. It is not a durable transfer coordinator.
/// </summary>
internal static class WorldPortalPersistedLocationPreflight
{
    private static readonly TimeSpan s_observationTimeout = TimeSpan.FromSeconds(10);

    internal static async ValueTask<WorldPortalPersistedLocationReservation?> TryReserveAsync(
        WorldRuntime runtime, MapTransitionCatalog transitions, CharacterLoginProfile authenticatedProfile,
        WorldCharacterOwnership ownership, ICharacterLocationObservationStore observations,
        ushort candidateX, ushort candidateY, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(authenticatedProfile);
        ArgumentNullException.ThrowIfNull(observations);
        cancellationToken.ThrowIfCancellationRequested();

        // A caller must supply its trusted login identity, never the identity claimed by a packet.
        // The runtime validates the generation independently when reserving the destination.
        if (authenticatedProfile.Identity.CharacterId != ownership.CharacterId ||
            authenticatedProfile.Location.MapId != ownership.MapId)
        {
            return null;
        }

        WorldTransferReservation? reservation = await WorldPortalPreflight.TryReserveAsync(
            runtime, transitions, ownership, candidateX, candidateY, cancellationToken).ConfigureAwait(false);
        if (reservation is null)
        {
            return null;
        }

        try
        {
            // The source barrier has completed; positional/avatar mutations are fenced.
            // Bound even a misbehaving observation adapter that ignores cancellation.
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(s_observationTimeout);

            CharacterLocation? persisted;
            try
            {
                persisted = await observations.ObserveAsync(authenticatedProfile.Identity.AccountId,
                    ownership.CharacterId, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                throw new TimeoutException("Persisted portal-source observation exceeded its deadline.", exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (persisted is null || persisted.MapId != ownership.MapId || !reservation.IsActive)
            {
                reservation.Dispose();
                return null;
            }

            // Persisted coordinates need not equal the live snapshot: walking is not durably
            // saved yet. The caller must retain both independently and never infer a CAS outcome.
            return new WorldPortalPersistedLocationReservation(reservation, persisted);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }
}
