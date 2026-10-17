using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Resolves a native 5517 action-85 candidate tile through the authoritative source
/// map's DMap exit markers and a validated, world-specific destination catalog.
/// Does not authorize the caller, commit a transfer, or send a client packet.
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

        // The catalog must have been validated against this exact immutable world snapshot.
        // Matching map and passway IDs alone cannot establish destination authority.
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
