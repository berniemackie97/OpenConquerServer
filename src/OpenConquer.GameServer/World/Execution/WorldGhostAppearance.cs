namespace OpenConquer.GameServer.World.Execution;

/// <summary>Older TQ server's lookface transform, consumed by the native 5517 ghost model.</summary>
internal static class WorldGhostAppearance
{
    public static uint FromBaseLook(uint baseLook)
    {
        // A face composite can exceed 10,000,000; only zero is invalid here.
        if (baseLook == 0)
        {
            throw new InvalidDataException("Ghost transformation requires a nonzero base lookface.");
        }

        uint sexDigit = baseLook % 10_000 / 1_000;
        return checked(baseLook + (sexDigit == 1 ? 980_000_000u : 990_000_000u));
    }
}
