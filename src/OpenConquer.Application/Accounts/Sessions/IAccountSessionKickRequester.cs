using OpenConquer.Domain.Realms;

namespace OpenConquer.Application.Accounts.Sessions;

public interface IAccountSessionKickRequester
{
    /// <summary>
    /// When the account is playing in a realm other than <paramref name="selectedRealm"/> and that realm is reporting
    /// status, asks its GameServer to disconnect the account and returns <see langword="true"/>.
    /// </summary>
    ValueTask<bool> TryKickFromOtherRealmAsync(uint accountId, RealmId selectedRealm, CancellationToken cancellationToken = default);
}
