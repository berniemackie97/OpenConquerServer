namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

public interface IGameDatabaseReadinessVerifier
{
    Task VerifyAsync(CancellationToken cancellationToken = default);
}
