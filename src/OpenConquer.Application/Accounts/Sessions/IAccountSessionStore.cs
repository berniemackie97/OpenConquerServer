namespace OpenConquer.Application.Accounts.Sessions;

/// <summary>
/// A GameServer's view of where each account is playing, bound to that GameServer's realm.
/// </summary>
public interface IAccountSessionStore
{
    /// <summary>
    /// Records the account as playing in this realm under <paramref name="sessionUid"/>, replacing any earlier
    /// record of it in this realm or in a realm that is no longer reporting status. Returns <see langword="false"/>,
    /// after asking that realm to disconnect it, when the account is still playing in another realm.
    /// </summary>
    ValueTask<bool> TryEnterAsync(uint accountId, uint characterId, uint sessionUid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the account's record if it still belongs to this realm and session.
    /// </summary>
    ValueTask LeaveAsync(uint accountId, uint sessionUid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the requests other logins have made for this realm to disconnect an account.
    /// </summary>
    ValueTask<IReadOnlyList<AccountSessionKick>> TakeKicksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every session and kick request recorded for this realm. Used when the GameServer starts and stops.
    /// </summary>
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}
