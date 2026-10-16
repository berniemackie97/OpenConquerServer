namespace OpenConquer.GameServer.World.Execution;

/// <summary>Older TQ server's lookface transform, consumed by the native 5517 ghost model.</summary>
internal static class WorldGhostAppearance
{
    public static uint FromBaseLook(uint baseLook)
    {
        // The base body/look composite must not already include a transformation family.
        if (baseLook is 0 or >= 10_000_000)
        {
            throw new InvalidDataException("Ghost transformation requires an untransformed base lookface.");
        }

        uint sexDigit = baseLook % 10_000 / 1_000;
        return checked(baseLook + (sexDigit == 1 ? 980_000_000u : 990_000_000u));
    }
}
