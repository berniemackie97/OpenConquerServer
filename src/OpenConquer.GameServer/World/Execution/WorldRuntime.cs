using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Coordinates character residency, exclusive partition execution, and world-session shutdown.
/// </summary>
internal sealed class WorldRuntime : IAsyncDisposable
{
    private readonly WorldScheduler _scheduler;
    private readonly Dictionary<uint, OwnershipEntry> _owners = [];
    private readonly Dictionary<uint, int> _mapPopulation = [];
    private readonly Dictionary<WorldCharacterOwnership, Task> _sessions = [];
    private readonly HashSet<Task> _admissions = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stoppingSignal = new();
    private readonly Guid _runtimeId = Guid.NewGuid();
    private readonly int _maxCharacters;
    private readonly int _maxCharactersPerMap;
    private ulong _nextGeneration;
    private bool _started;
    private bool _stopping;
    private Task? _stopTask;

    public WorldRuntime(WorldMapContentCatalog maps, WorldExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scheduler = new WorldScheduler(maps, options, HandleSchedulerWorkerFailure);
        _maxCharacters = options.MaxCharacters;
        _maxCharactersPerMap = options.MaxCharactersPerMap;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _started && !_stopping && _scheduler.IsRunning;
            }
        }
    }

    public CancellationToken StoppingToken => _stoppingSignal.Token;

    public int MaxCharacters => _maxCharacters;

    /// <summary>
    /// Gets the number of characters holding a reserved or active world residency.
    /// </summary>
    public int ResidentCount
    {
        get
        {
            lock (_gate)
            {
                return _owners.Count;
            }
        }
    }

    /// <summary>
    /// Gets whether any map partition is refusing new commands because its queue is full.
    /// </summary>
    public bool IsCongested => _scheduler.HasSaturatedPartition();

    public bool TryGetMap(uint mapId, [NotNullWhen(true)] out WorldMapContent? map)
    {
        if (_scheduler.TryGetPartition(mapId, out WorldPartition? partition))
        {
            map = partition.Map;
            return true;
        }

        map = null;
        return false;
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

    internal void TrackAdmission(Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        lock (_gate)
        {
            ThrowIfNotRunning();

            if (_admissions.Count >= _maxCharacters)
            {
                throw new InvalidOperationException("World-session admission capacity is exhausted.");
            }

            if (!_admissions.Add(completion))
            {
                throw new InvalidOperationException("The world-session admission is already tracked.");
            }
        }
    }

    internal void UntrackAdmission(Task completion)
    {
        lock (_gate)
        {
            _admissions.Remove(completion);
        }
    }

    internal void TrackSession(WorldCharacterOwnership ownership, Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        lock (_gate)
        {
            ThrowIfNotRunning();

            if (!_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) || entry.Ownership != ownership || entry.Removing)
            {
                throw new InvalidOperationException("The world session does not have a valid authoritative ownership reservation.");
            }

            if (_sessions.Count >= _maxCharacters)
            {
                throw new InvalidOperationException("World-session lifetime capacity is exhausted.");
            }

            if (!_sessions.TryAdd(ownership, completion))
            {
                throw new InvalidOperationException("The authoritative character already has a registered world session.");
            }
        }
    }

    internal void UntrackSession(WorldCharacterOwnership ownership, Task completion)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(ownership, out Task? tracked) && ReferenceEquals(tracked, completion))
            {
                _sessions.Remove(ownership);
            }
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

            if (_owners.Count >= _maxCharacters || _mapPopulation.GetValueOrDefault(partition.Map.MapId) >= _maxCharactersPerMap)
            {
                throw new InvalidOperationException($"World character capacity is exhausted for map {partition.Map.MapId}.");
            }

            if (_nextGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException("World character ownership generations are exhausted.");
            }

            WorldCharacterOwnership ownership = new(characterId, partition.Map.MapId, ++_nextGeneration, _runtimeId);
            WorldPartitionCommand<WorldCharacterOwnership> command = new(
                map =>
                {
                    map.Admit(ownership, profile.Location.X, profile.Location.Y);
                    return ownership;
                },
                _ => ReleaseOwner(ownership));

            _owners.Add(characterId, new OwnershipEntry(ownership));
            _mapPopulation[ownership.MapId] = _mapPopulation.GetValueOrDefault(ownership.MapId) + 1;

            switch (partition.TryEnqueue(command))
            {
                case WorldPartitionEnqueueResult.Accepted:
                    return new ValueTask<WorldCharacterOwnership>(command.Completion);
                case WorldPartitionEnqueueResult.CapacityExhausted:
                    ReleaseOwner(ownership);
                    throw new InvalidOperationException($"World partition {partition.Map.MapId} command queue is full.");
                default:
                    ReleaseOwner(ownership);
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

            if (!_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) || entry.Ownership != ownership || entry.Removing)
            {
                return ValueTask.FromResult(false);
            }

            if (_sessions.ContainsKey(ownership))
            {
                throw new InvalidOperationException($"Character {ownership.CharacterId} is owned by an active world session.");
            }

            return new ValueTask<bool>(QueueRemoval(entry));
        }
    }

    /// <summary>
    /// Releases authoritative residency independently of caller cancellation and without awaiting the calling session's completion.
    /// </summary>
    public async ValueTask ReleaseCharacterAsync(WorldCharacterOwnership ownership)
    {
        Task<bool>? removal = null;
        bool stopping;

        lock (_gate)
        {
            stopping = _stopping || !_started || !_scheduler.IsRunning;

            if (!stopping && _owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) && entry.Ownership == ownership)
            {
                removal = entry.RemovalTask ?? QueueRemoval(entry);
            }
        }

        if (stopping)
        {
            await _scheduler.StopAsync().ConfigureAwait(false);
            return;
        }

        if (removal is null)
        {
            return;
        }

        try
        {
            await removal.ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (!IsRunning)
        {
            await _scheduler.StopAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Queues one native 5517 eight-direction step under the exact active ownership token.
    /// This is not a substitute for authenticated packet validation, anti-replay, or movement pacing.
    /// </summary>
    public ValueTask<WorldCharacterSnapshot?> TryWalkCharacterAsync(WorldCharacterOwnership ownership, byte directionIndex, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();

            if (!_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) || entry.Ownership != ownership || entry.Removing || directionIndex >= 8)
            {
                return ValueTask.FromResult<WorldCharacterSnapshot?>(null);
            }

            if (!_scheduler.TryGetPartition(ownership.MapId, out WorldPartition? partition))
            {
                throw new InvalidOperationException("An active character references a missing world partition.");
            }

            WorldPartitionCommand<WorldCharacterSnapshot?> command = new(map => map.TryWalk(ownership, directionIndex));

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

    /// <summary>
    /// Initiates full shutdown while returning only the partition-drain task, avoiding a cycle with session completion.
    /// </summary>
    internal Task StopPartitionExecutionAsync()
    {
        _ = StopAsync();
        return _scheduler.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _stoppingSignal.Dispose();
        }
    }

    private void HandleSchedulerWorkerFailure(Exception exception)
    {
        _ = StopAsync();
    }

    private async Task StopCoreAsync()
    {
        List<Exception> failures = [];

        try
        {
            await _stoppingSignal.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AddFailure(failures, exception);
        }

        try
        {
            await _scheduler.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AddFailure(failures, exception);
        }

        Task[] lifetimes;

        lock (_gate)
        {
            _owners.Clear();
            _mapPopulation.Clear();
            lifetimes = [.. _sessions.Values, .. _admissions];
        }

        Task drain = Task.WhenAll(lifetimes);

        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (drain.Exception is AggregateException aggregate)
            {
                AddFailure(failures, aggregate);
            }
            else
            {
                AddFailure(failures, exception);
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("World execution shutdown and supervised session cleanup encountered multiple failures.", failures);
        }
    }

    private Task<bool> QueueRemoval(OwnershipEntry entry)
    {
        WorldCharacterOwnership ownership = entry.Ownership;

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
        entry.RemovalTask = command.Completion;

        try
        {
            if (partition.TryEnqueueRemoval(command) == WorldPartitionEnqueueResult.Accepted)
            {
                return command.Completion;
            }
        }
        catch
        {
            entry.Removing = false;
            entry.RemovalTask = null;
            throw;
        }

        entry.Removing = false;
        entry.RemovalTask = null;
        throw new InvalidOperationException($"World partition {partition.Map.MapId} is not accepting character removals.");
    }

    private void ReleaseOwner(WorldCharacterOwnership ownership)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(ownership.CharacterId, out OwnershipEntry? entry) && entry.Ownership == ownership)
            {
                _owners.Remove(ownership.CharacterId);
                int remaining = _mapPopulation[ownership.MapId] - 1;

                if (remaining == 0)
                {
                    _mapPopulation.Remove(ownership.MapId);
                }
                else
                {
                    _mapPopulation[ownership.MapId] = remaining;
                }
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
                entry.RemovalTask = null;
            }
        }
    }

    private static void AddFailure(List<Exception> failures, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(exception);
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

        public Task<bool>? RemovalTask
        {
            get; set;
        }

        public bool Removing
        {
            get; set;
        }
    }
}
