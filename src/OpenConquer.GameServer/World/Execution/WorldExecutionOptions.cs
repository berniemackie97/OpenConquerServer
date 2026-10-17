namespace OpenConquer.GameServer.World.Execution;

internal sealed class WorldExecutionOptions
{
    public WorldExecutionOptions(int workerCount, int partitionQueueCapacity, int maxCommandsPerTurn)
        : this(workerCount, partitionQueueCapacity, maxCommandsPerTurn, maxCharacters: 8192, maxCharactersPerMap: 1024)
    {
    }

    public WorldExecutionOptions(int workerCount, int partitionQueueCapacity, int maxCommandsPerTurn, int maxCharacters, int maxCharactersPerMap, TimeSpan? transferPreflightTimeout = null)
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
        TimeSpan timeout = transferPreflightTimeout ?? TimeSpan.FromSeconds(10);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(transferPreflightTimeout),
                "Transfer preflight timeout must be positive and no longer than 30 seconds.");
        }
        TransferPreflightTimeout = timeout;
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

    public TimeSpan TransferPreflightTimeout { get; }

    public int MaxCharactersPerMap
    {
        get;
    }
}
