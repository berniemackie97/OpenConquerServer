using System.Data.Common;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Accounts.Sessions;

namespace OpenConquer.GameServer.Realms;

/// <summary>
/// Records which accounts are playing in this realm, so that an account plays in only one realm at a time.
/// TQ's AccountServer kept the same record in its online table, filled by its world servers.
/// </summary>
internal sealed partial class RealmAccountSessions(IAccountSessionStore store, ILogger<RealmAccountSessions> logger)
{
    private readonly IAccountSessionStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<RealmAccountSessions> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Records the account as playing in this realm. Returns <see langword="null"/> when the account is still
    /// playing in another realm; that realm has then been asked to disconnect it.
    /// </summary>
    public async ValueTask<AccountSessionLease?> TryEnterAsync(uint accountId, uint characterId, uint sessionUid, CancellationToken cancellationToken = default)
    {
        bool entered = await _store.TryEnterAsync(accountId, characterId, sessionUid, cancellationToken).ConfigureAwait(false);

        return entered ? new AccountSessionLease(this, accountId, sessionUid) : null;
    }

    /// <summary>
    /// Removes the account's record. A record that cannot be removed is not fatal: it only matters once the account
    /// logs in again, and both a later login to this realm and a kick request from another realm replace it.
    /// </summary>
    internal async ValueTask ReleaseAsync(uint accountId, uint sessionUid)
    {
        try
        {
            await _store.LeaveAsync(accountId, sessionUid, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            LogReleaseFailed(_logger, accountId, exception);
        }
    }

    [LoggerMessage(EventId = 6310, Level = LogLevel.Warning, Message = "The session record of account {AccountId} could not be removed.")]
    private static partial void LogReleaseFailed(ILogger logger, uint accountId, Exception exception);
}
