namespace OpenConquer.GameData.Tool.Maps;

internal sealed class MapCollisionSourceLimits
{
    public MapCollisionSourceLimits(int maximumScenerySubcomponents, int maximumObjectListsPerTerrain,
        int maximumObjectsPerList, long maximumCellsPerList, int maximumObjectFileBytes,
        long maximumAttachmentsPerTerrain, long maximumTotalAttachments, long maximumTotalObjectBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScenerySubcomponents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumObjectListsPerTerrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumObjectsPerList);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCellsPerList);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumObjectFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttachmentsPerTerrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalAttachments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalObjectBytes);

        MaximumScenerySubcomponents = maximumScenerySubcomponents;
        MaximumObjectListsPerTerrain = maximumObjectListsPerTerrain;
        MaximumObjectsPerList = maximumObjectsPerList;
        MaximumCellsPerList = maximumCellsPerList;
        MaximumObjectFileBytes = maximumObjectFileBytes;
        MaximumAttachmentsPerTerrain = maximumAttachmentsPerTerrain;
        MaximumTotalAttachments = maximumTotalAttachments;
        MaximumTotalObjectBytes = maximumTotalObjectBytes;
    }

    public int MaximumScenerySubcomponents
    {
        get;
    }

    public int MaximumObjectListsPerTerrain
    {
        get;
    }

    public int MaximumObjectsPerList
    {
        get;
    }

    public long MaximumCellsPerList
    {
        get;
    }

    public int MaximumObjectFileBytes
    {
        get;
    }

    public long MaximumAttachmentsPerTerrain
    {
        get;
    }

    public long MaximumTotalAttachments
    {
        get;
    }

    public long MaximumTotalObjectBytes
    {
        get;
    }

    public static MapCollisionSourceLimits CreateDefault() => new(maximumScenerySubcomponents: 100_000, maximumObjectListsPerTerrain: 10_000, maximumObjectsPerList: 10_000, maximumCellsPerList: 2_000_000, maximumObjectFileBytes: 64 * 1024 * 1024, maximumAttachmentsPerTerrain: 8_000_000, maximumTotalAttachments: 100_000_000, maximumTotalObjectBytes: 4L * 1024 * 1024 * 1024);
}
