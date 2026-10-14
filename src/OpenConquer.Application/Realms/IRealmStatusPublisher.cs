using OpenConquer.Domain.Realms;

namespace OpenConquer.Application.Realms;

/// <summary>
/// Publishes the running GameServer's realm status for the AccountServer.
/// </summary>
public interface IRealmStatusPublisher
{
    /// <summary>
    /// Replaces the realm's status report. The report expires after <paramref name="lifetime"/>
    /// unless it is published again.
    /// </summary>
    ValueTask PublishAsync(RealmLoad load, int onlineCount, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the realm's status report, so that the realm is treated as down immediately.
    /// </summary>
    ValueTask WithdrawAsync(CancellationToken cancellationToken = default);
}
