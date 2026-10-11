using System.Threading.Channels;
using OpenConquer.Application.World;

namespace OpenConquer.GameServer.World.Execution;

internal enum WorldPartitionEnqueueResult
{
    Accepted,
    CapacityExhausted,
    Stopped,
}

/// <summary>
/// Serializes bounded map-local commands and yields to the shared scheduler after each execution quantum.
/// </summary>
internal sealed class WorldPartition
{
    private readonly MapInstance _map;
    private readonly ChannelWriter<WorldPartition> _ready;
    private readonly Queue<IWorldPartitionCommand> _pending = [];
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly int _maxCommandsPerTurn;
    private bool _scheduled;
    private bool _executing;
    private bool _stopped;

    public WorldPartition(WorldMapContent map, ChannelWriter<WorldPartition> ready, int capacity, int maxCommandsPerTurn)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommandsPerTurn, 1);

        _map = new MapInstance(map);
        _ready = ready;
        _capacity = capacity;
        _maxCommandsPerTurn = maxCommandsPerTurn;
    }

    public WorldMapContent Map => _map.Map;

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

            if (!_scheduled)
            {
                _scheduled = true;

                if (!_ready.TryWrite(this))
                {
                    _scheduled = false;
                    _pending.Dequeue();
                    return WorldPartitionEnqueueResult.Stopped;
                }
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
                    if (_stopped || _pending.Count == 0)
                    {
                        break;
                    }

                    command = _pending.Dequeue();
                }

                command.Execute(_map);
            }
        }
        finally
        {
            lock (_gate)
            {
                _executing = false;

                if (_stopped || _pending.Count == 0)
                {
                    _scheduled = false;
                }
                else if (!_ready.TryWrite(this))
                {
                    _stopped = true;
                    _scheduled = false;
                    rejected = [.. _pending];
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
            rejected = [.. _pending];
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
}
