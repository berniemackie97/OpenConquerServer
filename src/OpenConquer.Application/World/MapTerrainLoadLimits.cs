namespace OpenConquer.Application.World;

/// <summary>
/// Resource limits shared by offline terrain conversion and canonical runtime loading.
/// </summary>
public sealed class MapTerrainLoadLimits
{
    public MapTerrainLoadLimits(int maximumWidth, int maximumHeight, int maximumCellsPerTerrain,
        long maximumTotalCells, int maximumExitsPerTerrain, int maximumArtifactBytes, long maximumContainerBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCellsPerTerrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExitsPerTerrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumContainerBytes);

        if (maximumCellsPerTerrain > int.MaxValue / 6)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCellsPerTerrain), "Terrain cell capacity exceeds the supported binary resource budget.");
        }

        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        MaximumCellsPerTerrain = maximumCellsPerTerrain;
        MaximumTotalCells = maximumTotalCells;
        MaximumExitsPerTerrain = maximumExitsPerTerrain;
        MaximumArtifactBytes = maximumArtifactBytes;
        MaximumContainerBytes = maximumContainerBytes;
    }

    public int MaximumWidth { get; }
    public int MaximumHeight { get; }
    public int MaximumCellsPerTerrain { get; }
    public long MaximumTotalCells { get; }
    public int MaximumExitsPerTerrain { get; }
    public int MaximumArtifactBytes { get; }
    public long MaximumContainerBytes { get; }

    public static MapTerrainLoadLimits CreateDefault() => new(maximumWidth: 4096, maximumHeight: 4096, maximumCellsPerTerrain: 8_000_000,
            maximumTotalCells: 100_000_000, maximumExitsPerTerrain: 65_536,
            maximumArtifactBytes: 64 * 1024 * 1024, maximumContainerBytes: 128L * 1024 * 1024);
}
