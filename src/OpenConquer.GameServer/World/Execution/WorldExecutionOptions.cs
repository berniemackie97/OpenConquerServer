namespace OpenConquer.GameServer.World.Execution;

internal sealed class WorldExecutionOptions
{
    public WorldExecutionOptions(int workerCount, int partitionQueueCapacity, int maxCommandsPerTurn)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionQueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommandsPerTurn, 1);

        WorkerCount = workerCount;
        PartitionQueueCapacity = partitionQueueCapacity;
        MaxCommandsPerTurn = maxCommandsPerTurn;
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
}
