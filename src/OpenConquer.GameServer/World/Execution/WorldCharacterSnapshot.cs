namespace OpenConquer.GameServer.World.Execution;

internal readonly record struct WorldCharacterSnapshot(
    WorldCharacterOwnership Ownership, ushort X, ushort Y, bool IsAlive = true,
    WorldPrimaryStatusMask Status = default, long DeadSinceTimestamp = 0)
{
    public bool IsGhost => !IsAlive && Status.Has(WorldPrimaryStatusMask.Dead) && Status.Has(WorldPrimaryStatusMask.Ghost);

    public bool CanWalk => (IsAlive || IsGhost) &&
        !Status.Has(WorldPrimaryStatusMask.Flustered) &&
        !Status.Has(WorldPrimaryStatusMask.Stunned) &&
        !Status.Has(WorldPrimaryStatusMask.Frozen);
}
