namespace OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

internal sealed class AccountDatabaseReadinessException : InvalidOperationException
{
    public AccountDatabaseReadinessException(string message) : base(message) { }
}
