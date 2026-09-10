namespace OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

public interface IAccountDatabaseReadinessVerifier
{
    Task VerifyAsync(CancellationToken cancellationToken = default);
}
