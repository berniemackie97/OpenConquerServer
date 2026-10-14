using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using OpenConquer.Application.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Schedules exclusive map-partition turns across a fixed worker pool and preserves all terminal execution failures.
/// </summary>
internal sealed class WorldScheduler : IAsyncDisposable
{
    private readonly FrozenDictionary<uint, WorldPartition> _partitions;
    private readonly Channel<WorldPartition> _ready;
    private readonly Action<Exception>? _onWorkerFailure;
    private readonly Lock _gate = new();
    private readonly int _workerCount;
    private Task[] _workers = [];
    private Task? _stopTask;
    private int _state;

    public WorldScheduler(WorldMapContentCatalog maps, WorldExecutionOptions options, Action<Exception>? onWorkerFailure = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(options);

        _workerCount = options.WorkerCount;
        _onWorkerFailure = onWorkerFailure;

        _ready = Channel.CreateBounded<WorldPartition>(new BoundedChannelOptions(maps.Count)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        Dictionary<uint, WorldPartition> partitions = [];

        foreach (WorldMapContent map in maps.Maps)
        {
            partitions.Add(map.MapId, new WorldPartition(map, _ready.Writer, options.PartitionQueueCapacity, options.MaxCommandsPerTurn,
                options.MaxCharactersPerMap));
        }

        _partitions = partitions.ToFrozenDictionary();
    }

    public bool IsRunning => Volatile.Read(ref _state) == 1;

    public bool TryGetPartition(uint mapId, [NotNullWhen(true)] out WorldPartition? partition)
    {
        return _partitions.TryGetValue(mapId, out partition);
    }

    public bool HasSaturatedPartition()
    {
        foreach (WorldPartition partition in _partitions.Values)
        {
            if (partition.IsSaturated)
            {
                return true;
            }
        }

        return false;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_state != 0)
            {
                throw new InvalidOperationException("The world scheduler can only be started once.");
            }

            Volatile.Write(ref _state, 1);
            _workers = new Task[_workerCount];

            for (int index = 0; index < _workers.Length; index++)
            {
                _workers[index] = Task.Run(ExecuteAsync);
            }
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            Volatile.Write(ref _state, 2);
            _stopTask = Task.Run(StopCoreAsync);
            return _stopTask;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task ExecuteAsync()
    {
        try
        {
            await foreach (WorldPartition partition in _ready.Reader.ReadAllAsync())
            {
                partition.ExecuteTurn();
            }
        }
        catch (Exception exception)
        {
            _ = StopAsync();
            _onWorkerFailure?.Invoke(exception);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        List<Exception> failures = [];

        foreach (WorldPartition partition in _partitions.Values)
        {
            try
            {
                partition.Stop();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        _ready.Writer.TryComplete();

        Task workers = Task.WhenAll(_workers);

        try
        {
            await workers.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (workers.Exception is { } aggregate)
            {
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            }
            else
            {
                failures.Add(exception);
            }
        }

        try
        {
            foreach (WorldPartition partition in _partitions.Values)
            {
                try
                {
                    partition.Clear();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _state, 3);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("World scheduler shutdown encountered multiple failures.", failures);
        }
    }
}
