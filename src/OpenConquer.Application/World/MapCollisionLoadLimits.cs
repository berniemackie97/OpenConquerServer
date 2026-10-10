namespace OpenConquer.Application.World;

public sealed class MapCollisionLoadLimits
{
    public MapCollisionLoadLimits(int maximumOverridesPerTerrain, long maximumTotalOverrides, int maximumArtifactBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOverridesPerTerrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalOverrides);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArtifactBytes);

        if (maximumArtifactBytes < 88)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes), "Collision artifacts require at least 88 bytes.");
        }

        MaximumOverridesPerTerrain = maximumOverridesPerTerrain;
        MaximumTotalOverrides = maximumTotalOverrides;
        MaximumArtifactBytes = maximumArtifactBytes;
    }

    public int MaximumOverridesPerTerrain
    {
        get;
    }

    public long MaximumTotalOverrides
    {
        get;
    }

    public int MaximumArtifactBytes
    {
        get;
    }

    public static MapCollisionLoadLimits CreateDefault() => new(maximumOverridesPerTerrain: 4_000_000, maximumTotalOverrides: 10_000_000, maximumArtifactBytes: 64 * 1024 * 1024);
}
