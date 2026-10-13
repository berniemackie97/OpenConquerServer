using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

public interface IGameRealmBindingVerifier
{
    Task VerifyAsync(RealmId expectedRealmId, CancellationToken cancellationToken = default);
}
