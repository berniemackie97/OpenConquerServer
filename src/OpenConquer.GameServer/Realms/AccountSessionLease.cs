namespace OpenConquer.GameServer.Realms;

/// <summary>
/// One login's record that its account is playing in this realm. Disposing removes the record.
/// </summary>
internal sealed class AccountSessionLease : IAsyncDisposable
{
    private readonly RealmAccountSessions _sessions;
    private int _disposed;

    internal AccountSessionLease(RealmAccountSessions sessions, uint accountId, uint sessionUid)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        AccountId = accountId;
        SessionUid = sessionUid;
    }

    public uint AccountId { get; }
    public uint SessionUid { get; }

    public ValueTask DisposeAsync()
    {
        return Interlocked.Exchange(ref _disposed, 1) == 0
            ? _sessions.ReleaseAsync(AccountId, SessionUid)
            : ValueTask.CompletedTask;
    }
}
