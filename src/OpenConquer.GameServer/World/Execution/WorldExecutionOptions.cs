namespace OpenConquer.GameServer.World.Execution;

internal sealed class WorldExecutionOptions
{
    public WorldExecutionOptions(int workerCount, int partitionQueueCapacity, int maxCommandsPerTurn)
        : this(workerCount, partitionQueueCapacity, maxCommandsPerTurn, maxCharacters: 8192, maxCharactersPerMap: 1024)
    {
    }

    public WorldExecutionOptions(int workerCount, int partitionQueueCapacity, int maxCommandsPerTurn, int maxCharacters, int maxCharactersPerMap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionQueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommandsPerTurn, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharactersPerMap, 1);

        WorkerCount = workerCount;
        PartitionQueueCapacity = partitionQueueCapacity;
        MaxCommandsPerTurn = maxCommandsPerTurn;
        MaxCharacters = maxCharacters;
        MaxCharactersPerMap = maxCharactersPerMap;
    }

    public int WorkerCount
    {
        get;
    }

    public int PartitionQueueCapacity
    {
        get;
    }

    public int MaxCommandsPerTurn
    {
        get;
    }

    public int MaxCharacters
    {
        get;
    }

    public int MaxCharactersPerMap
    {
        get;
    }
}
