namespace OpenConquer.GameServer.World.Execution;

/// <summary>Native 5517 primary 128-bit status mask. Only authoritative world commands mutate it.</summary>
internal readonly record struct WorldPrimaryStatusMask(ulong Low, ulong High)
{
    public const int Dead = 5;
    public const int Ghost = 10;
    public const int Flustered = 0x36;
    public const int Stunned = 0x3A;
    public const int Frozen = 0x3B;

    public bool Has(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, 128);
        return bit < 64 ? (Low & (1UL << bit)) != 0 : (High & (1UL << (bit - 64))) != 0;
    }

    public WorldPrimaryStatusMask With(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, 128);
        return bit < 64 ? this with { Low = Low | (1UL << bit) }
            : this with { High = High | (1UL << (bit - 64)) };
    }

    public static WorldPrimaryStatusMask ForAdmission(bool alive) =>
        alive ? default : default(WorldPrimaryStatusMask).With(Dead);
}
