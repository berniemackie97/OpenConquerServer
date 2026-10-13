using OpenConquer.Domain.Realms;

namespace OpenConquer.Application.Realms;

public interface IRealmRegistry
{
    ValueTask<RealmRegistration?> FindByNameAsync(ProtocolServerName protocolName, CancellationToken cancellationToken = default);
}
