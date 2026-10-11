using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Coordinates exclusive character ownership across canonical map partitions.
/// Accepted operations complete independently of subsequent caller cancellation.
/// </summary>
internal sealed class WorldRuntime : IAsyncDisposable
{
    private readonly WorldScheduler _scheduler;
    private readonly Dictionary<uint, OwnershipEntry> _owners = [];
    private readonly Lock _gate = new();
    private ulong _nextGeneration;
    private bool _started;
    private bool _stopping;
    private Task? _stopTask;

    public WorldRuntime(WorldMapContentCatalog maps, WorldExecutionOptions options)
    {
        _scheduler = new WorldScheduler(maps, options);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopping)
            {
                throw new InvalidOperationException("The world runtime can only be started once.");
            }

            _scheduler.Start();
            _started = true;
        }
    }

    public ValueTask<WorldCharacterOwnership> AdmitCharacterAsync(CharacterLoginProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();

            uint characterId = profile.Identity.CharacterId;

            if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
            {
                throw new ArgumentOutOfRangeException(nameof(profile), "World admission requires a valid player character ID.");
            }

            if (!_scheduler.TryGetPartition(profile.Location.MapId, out WorldPartition? partition))
            {
                throw new InvalidDataException($"Character {characterId} references unavailable world map {profile.Location.MapId}.");
            }

            if (profile.Location.X >= partition.Map.Collision.Width || profile.Location.Y >= partition.Map.Collision.Height)
            {
                throw new InvalidDataException($"Character {characterId} has an out-of-bounds persisted position on map {partition.Map.MapId}.");
            }

            if (_owners.ContainsKey(characterId))
            {
                throw new InvalidOperationException($"Character {characterId} already has a reserved or active world owner.");
            }

            if (_nextGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException("World character ownership generations are exhausted.");
            }

            WorldCharacterOwnership ownership = new(characterId, partition.Map.MapId, ++_nextGeneration);
            WorldPartitionCommand<WorldCharacterOwnership> command = new(
                map =>
                {
                    map.Admit(ownership, profile.Location.X, profile.Location.Y);
                    return ownership;
                },
                _ => ReleaseOwner(ownership));

            _owners.Add(characterId, new OwnershipEntry(ownership));

            switch (partition.TryEnqueue(command))
            {
                case WorldPartitionEnqueueResult.Accepted:
                    return new ValueTask<WorldCharacterOwnership>(command.Completion);
                case WorldPartitionEnqueueResult.CapacityExhausted:
                    _owners.Remove(characterId);
                    throw new InvalidOperationException($"World partition {partition.Map.MapId} command queue is full.");
                default:
                    _owners.Remove(characterId);
                    throw new InvalidOperationException($"World partition {partition.Map.MapId} is not accepting commands.");
            }
        }
    }

    public ValueTask<bool> RemoveCharacterAsync(WorldCharacterOwnership ownership, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();

            if (!_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry)
                || entry.Ownership != ownership || entry.Removing)
            {
                return ValueTask.FromResult(false);
            }

            if (!_scheduler.TryGetPartition(ownership.MapId, out WorldPartition? partition))
            {
                throw new InvalidOperationException("An active character references a missing world partition.");
            }

            WorldPartitionCommand<bool> command = new(
                map =>
                {
                    if (!map.Remove(ownership))
                    {
                        throw new InvalidOperationException($"World partition {ownership.MapId} lost character {ownership.CharacterId} ownership.");
                    }

                    ReleaseOwner(ownership);
                    return true;
                },
                _ => RestoreRemoval(ownership));

            entry.Removing = true;

            switch (partition.TryEnqueue(command))
            {
                case WorldPartitionEnqueueResult.Accepted:
                    return new ValueTask<bool>(command.Completion);
                case WorldPartitionEnqueueResult.CapacityExhausted:
                    entry.Removing = false;
                    throw new InvalidOperationException($"World partition {partition.Map.MapId} command queue is full.");
                default:
                    entry.Removing = false;
                    throw new InvalidOperationException($"World partition {partition.Map.MapId} is not accepting commands.");
            }
        }
    }

    public ValueTask<WorldCharacterSnapshot?> ReadCharacterAsync(WorldCharacterOwnership ownership, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();

            if (!_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) || entry.Ownership != ownership)
            {
                return ValueTask.FromResult<WorldCharacterSnapshot?>(null);
            }

            if (!_scheduler.TryGetPartition(ownership.MapId, out WorldPartition? partition))
            {
                throw new InvalidOperationException("An active character references a missing world partition.");
            }

            WorldPartitionCommand<WorldCharacterSnapshot?> command = new(map => map.Find(ownership));

            return partition.TryEnqueue(command) switch
            {
                WorldPartitionEnqueueResult.Accepted => new ValueTask<WorldCharacterSnapshot?>(command.Completion),
                WorldPartitionEnqueueResult.CapacityExhausted => throw new InvalidOperationException($"World partition {partition.Map.MapId} command queue is full."),
                _ => throw new InvalidOperationException($"World partition {partition.Map.MapId} is not accepting commands."),
            };
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

            _stopping = true;
            _stopTask = Task.Run(StopCoreAsync);
            return _stopTask;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopCoreAsync()
    {
        try
        {
            await _scheduler.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _owners.Clear();
            }
        }
    }

    private void ReleaseOwner(WorldCharacterOwnership ownership)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) && entry.Ownership == ownership)
            {
                _owners.Remove(ownership.CharacterId);
            }
        }
    }

    private void RestoreRemoval(WorldCharacterOwnership ownership)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) && entry.Ownership == ownership)
            {
                entry.Removing = false;
            }
        }
    }

    private void ThrowIfNotRunning()
    {
        if (!_started || _stopping || !_scheduler.IsRunning)
        {
            throw new InvalidOperationException("The world runtime is not accepting commands.");
        }
    }

    private sealed class OwnershipEntry(WorldCharacterOwnership ownership)
    {
        public WorldCharacterOwnership Ownership { get; } = ownership;
        public bool Removing { get; set; }
    }
}
