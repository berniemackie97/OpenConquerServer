using OpenConquer.Domain.Realms;

namespace OpenConquer.Application.Realms;

public interface IRealmStatusDirectory
{
    /// <summary>
    /// Returns the load from the realm's latest unexpired status report, or <see langword="null"/>
    /// when its GameServer has not reported within the report lifetime.
    /// </summary>
    ValueTask<RealmLoad?> FindCurrentLoadAsync(RealmId realmId, CancellationToken cancellationToken = default);
}
