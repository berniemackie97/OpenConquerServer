using OpenConquer.Infrastructure.Security.Accounts.Registration;

namespace OpenConquer.AccountServer.Configuration;

internal sealed class AccountServerRegistrationConfiguration(string connectionString, AccountRegistrationProtectionOptions protection)
{
    public string ConnectionString { get; } = connectionString;
    public AccountRegistrationProtectionOptions Protection { get; } = protection;
}
