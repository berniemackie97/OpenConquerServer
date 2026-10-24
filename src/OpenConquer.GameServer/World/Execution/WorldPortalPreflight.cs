using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Resolves a 5517 action-85 portal tile using the source map's DMap exit markers and the world's transition
/// catalog. It doesn't check who is asking, move anyone or send anything to the client.
/// </summary>
internal static class WorldPortalPreflight
{
    internal static async ValueTask<WorldTransferReservation?> TryReserveAsync(WorldRuntime runtime,
        MapTransitionCatalog transitions, WorldCharacterOwnership ownership, ushort candidateX, ushort candidateY,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(transitions);
        cancellationToken.ThrowIfCancellationRequested();

        // The catalog must have been validated against this same world content; matching map and passway IDs alone
        // aren't enough.
        if (!runtime.UsesMapContent(transitions.Maps) ||
            !runtime.TryGetMap(ownership.MapId, out var source))
        {
            return null;
        }

        // The packet carries a tile, not a passway index or trusted destination.
        // Multiple exits at one tile would be ambiguous; refuse rather than pick one.
        uint passwayIndex = 0;
        bool found = false;
        foreach (MapTerrainExit exit in source.BaseTerrain.Exits)
        {
            if (exit.X != candidateX || exit.Y != candidateY)
            {
                continue;
            }

            if (found)
            {
                return null;
            }
            found = true;
            passwayIndex = exit.PasswayIndex;
        }

        if (!found || !transitions.TryResolve(ownership.MapId, passwayIndex, out MapTransitionDestination? destination) ||
            destination.X is < 0 or > ushort.MaxValue || destination.Y is < 0 or > ushort.MaxValue)
        {
            return null;
        }

        // This barrier is also the authorization point for the current source tile:
        // moves accepted before the request must be reflected in SourceSnapshot.
        WorldTransferReservation? reservation = await runtime.TryReserveTransferAsync(ownership,
            destination.MapId, (ushort)destination.X, (ushort)destination.Y, cancellationToken).ConfigureAwait(false);
        if (reservation is null)
        {
            return null;
        }

        try
        {
            WorldCharacterSnapshot current = reservation.SourceSnapshot;
            if (!reservation.IsActive || !current.IsAlive || current.IsGhost ||
                Math.Abs((int)current.X - candidateX) > 1 || Math.Abs((int)current.Y - candidateY) > 1)
            {
                reservation.Dispose();
                return null;
            }

            return reservation;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }
}
