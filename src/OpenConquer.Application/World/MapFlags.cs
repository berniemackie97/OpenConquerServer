namespace OpenConquer.Application.World;

/// <summary>
/// Projects preserved server map flags onto the 5517 client-visible flag bits.
/// The original source flags must remain unchanged in the canonical map definition.
/// </summary>
public static class MapFlags
{
    public const ulong ClientMask = (1UL << 41) - 1;

    public static ulong Project(ulong sourceFlags) => sourceFlags & ClientMask;
}
