using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using OpenConquer.Application.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Schedules ready map partitions across a fixed worker pool without concurrent turns on one partition.
/// Unexpected worker failure initiates shutdown and notifies the owning runtime.
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
        foreach (WorldPartition partition in _partitions.Values)
        {
            partition.Stop();
        }

        _ready.Writer.TryComplete();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        finally
        {
            foreach (WorldPartition partition in _partitions.Values)
            {
                partition.Clear();
            }

            Volatile.Write(ref _state, 3);
        }
    }
}
