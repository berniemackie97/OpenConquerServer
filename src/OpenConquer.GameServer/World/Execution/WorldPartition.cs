using System.Threading.Channels;
using OpenConquer.Application.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Runs one map's commands one turn at a time from a fixed-size FIFO queue, with a separate fixed-size lane
/// for removing characters.
/// </summary>
internal sealed class WorldPartition
{
    private readonly MapInstance _map;
    private readonly ChannelWriter<WorldPartition> _ready;
    private readonly Queue<IWorldPartitionCommand> _pending = [];
    private readonly Queue<IWorldPartitionCommand> _removals = [];
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly int _removalCapacity;
    private readonly int _maxCommandsPerTurn;
    private bool _scheduled;
    private bool _executing;
    private bool _stopped;

    public WorldPartition(WorldMapContent map, ChannelWriter<WorldPartition> ready, int capacity, int maxCommandsPerTurn, int removalCapacity = 1024)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(removalCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommandsPerTurn, 1);

        _map = new MapInstance(map);
        _ready = ready;
        _capacity = capacity;
        _removalCapacity = removalCapacity;
        _maxCommandsPerTurn = maxCommandsPerTurn;
    }

    public WorldMapContent Map => _map.Map;

    /// <summary>
    /// Gets whether the ordinary command queue is full, so that new commands are being refused.
    /// </summary>
    public bool IsSaturated
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count >= _capacity;
            }
        }
    }

    public WorldPartitionEnqueueResult TryEnqueue(IWorldPartitionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (_stopped)
            {
                return WorldPartitionEnqueueResult.Stopped;
            }

            if (_pending.Count >= _capacity)
            {
                return WorldPartitionEnqueueResult.CapacityExhausted;
            }

            _pending.Enqueue(command);

            if (!Schedule())
            {
                _pending.Dequeue();
                return WorldPartitionEnqueueResult.Stopped;
            }

            return WorldPartitionEnqueueResult.Accepted;
        }
    }

    /// <summary>
    /// Reserves cleanup work independently of the ordinary queue. The runtime admits no more than removalCapacity characters per map.
    /// </summary>
    public WorldPartitionEnqueueResult TryEnqueueRemoval(IWorldPartitionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (_stopped)
            {
                return WorldPartitionEnqueueResult.Stopped;
            }

            if (_removals.Count >= _removalCapacity)
            {
                throw new InvalidOperationException($"World partition {Map.MapId} exceeded its reserved character-removal capacity.");
            }

            _removals.Enqueue(command);

            if (!Schedule())
            {
                _removals.Dequeue();
                return WorldPartitionEnqueueResult.Stopped;
            }

            return WorldPartitionEnqueueResult.Accepted;
        }
    }

    public void ExecuteTurn()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            if (_executing)
            {
                throw new InvalidOperationException($"World partition {Map.MapId} is already executing.");
            }

            _executing = true;
        }

        List<IWorldPartitionCommand>? rejected = null;

        try
        {
            for (int processed = 0; processed < _maxCommandsPerTurn; processed++)
            {
                IWorldPartitionCommand command;

                lock (_gate)
                {
                    if (_stopped || (_pending.Count == 0 && _removals.Count == 0))
                    {
                        break;
                    }

                    command = _removals.Count != 0 ? _removals.Dequeue() : _pending.Dequeue();
                }

                command.Execute(_map);
            }
        }
        finally
        {
            lock (_gate)
            {
                _executing = false;

                if (_stopped || (_pending.Count == 0 && _removals.Count == 0))
                {
                    _scheduled = false;
                }
                else if (!_ready.TryWrite(this))
                {
                    _stopped = true;
                    _scheduled = false;
                    rejected = [.. _removals, .. _pending];
                    _removals.Clear();
                    _pending.Clear();
                }
            }

            if (rejected is not null)
            {
                InvalidOperationException failure = new($"World partition {Map.MapId} could not be rescheduled.");

                foreach (IWorldPartitionCommand command in rejected)
                {
                    command.Reject(failure);
                }

                throw failure;
            }
        }
    }

    public void Stop()
    {
        IWorldPartitionCommand[] rejected;

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _scheduled = false;
            rejected = [.. _removals, .. _pending];
            _removals.Clear();
            _pending.Clear();
        }

        InvalidOperationException failure = new($"World partition {Map.MapId} stopped before queued work could execute.");

        foreach (IWorldPartitionCommand command in rejected)
        {
            command.Reject(failure);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (!_stopped || _executing)
            {
                throw new InvalidOperationException("Map-local state can only be cleared after partition execution has stopped.");
            }

            _map.Clear();
        }
    }

    private bool Schedule()
    {
        if (_scheduled)
        {
            return true;
        }

        _scheduled = true;

        if (_ready.TryWrite(this))
        {
            return true;
        }

        _scheduled = false;
        return false;
    }
}
