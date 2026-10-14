using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.GameServer.Realms;

namespace OpenConquer.GameServer.Tests.Realms;

internal static class TestAccountSessions
{
    public static RealmAccountSessions Create(IAccountSessionStore? store = null) =>
        new(store ?? new RecordingAccountSessionStore(), NullLogger<RealmAccountSessions>.Instance);

    public static AccountSessionLease CreateLease(uint accountId, uint sessionUid, IAccountSessionStore? store = null) =>
        new(Create(store), accountId, sessionUid);
}

internal sealed class RecordingAccountSessionStore : IAccountSessionStore
{
    private readonly Lock _gate = new();
    private readonly List<(uint AccountId, uint CharacterId, uint SessionUid)> _entered = [];
    private readonly List<(uint AccountId, uint SessionUid)> _left = [];
    private readonly Queue<IReadOnlyList<AccountSessionKick>> _kicks = new();
    private int _resetCount;

    public bool AdmitEntries { get; init; } = true;
    public Exception? LeaveFailure { get; init; }
    public Exception? TakeKicksFailure { get; init; }
    public Exception? ResetFailure { get; init; }

    /// <summary>
    /// Runs after an entry is recorded and before <see cref="TryEnterAsync"/> returns.
    /// </summary>
    public Func<ValueTask>? OnEnter { get; set; }

    public IReadOnlyList<(uint AccountId, uint CharacterId, uint SessionUid)> Entered
    {
        get
        {
            lock (_gate)
            {
                return [.. _entered];
            }
        }
    }

    public IReadOnlyList<(uint AccountId, uint SessionUid)> Left
    {
        get
        {
            lock (_gate)
            {
                return [.. _left];
            }
        }
    }

    public int ResetCount => Volatile.Read(ref _resetCount);

    public void QueueKicks(params AccountSessionKick[] kicks)
    {
        lock (_gate)
        {
            _kicks.Enqueue(kicks);
        }
    }

    public async ValueTask<bool> TryEnterAsync(uint accountId, uint characterId, uint sessionUid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entered.Add((accountId, characterId, sessionUid));
        }

        if (OnEnter is { } onEnter)
        {
            await onEnter();
        }

        return AdmitEntries;
    }

    public ValueTask LeaveAsync(uint accountId, uint sessionUid, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _left.Add((accountId, sessionUid));
        }

        return LeaveFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(LeaveFailure);
    }

    public ValueTask<IReadOnlyList<AccountSessionKick>> TakeKicksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TakeKicksFailure is not null)
        {
            return ValueTask.FromException<IReadOnlyList<AccountSessionKick>>(TakeKicksFailure);
        }

        lock (_gate)
        {
            return ValueTask.FromResult(_kicks.Count == 0 ? (IReadOnlyList<AccountSessionKick>)[] : _kicks.Dequeue());
        }
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _resetCount);
        return ResetFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(ResetFailure);
    }
}
