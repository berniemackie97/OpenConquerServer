using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Registration;

/// <summary>
/// Validates the database identity used only for self-service account creation. Its grants are administered and
/// verified separately; see the operations guide.
/// </summary>
public static class AccountRegistrationConnectionPolicy
{
    public static void Validate(string accountConnectionString, string registrationConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationConnectionString);

        MySqlConnectionStringBuilder accounts;
        MySqlConnectionStringBuilder registration;

        try
        {
            accounts = new MySqlConnectionStringBuilder(accountConnectionString);
            registration = new MySqlConnectionStringBuilder(registrationConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // A driver exception can contain connection-string material; never propagate it.
            throw new InvalidOperationException("The AccountServer database connection settings are invalid.");
        }

        if (string.IsNullOrWhiteSpace(accounts.Database) || string.IsNullOrWhiteSpace(registration.Database)
            || !string.Equals(accounts.Database, registration.Database, StringComparison.Ordinal)
            || !string.Equals(accounts.Server, registration.Server, StringComparison.OrdinalIgnoreCase)
            || accounts.Port != registration.Port
            || accounts.ConnectionProtocol != registration.ConnectionProtocol
            || (accounts.ConnectionProtocol == MySqlConnectionProtocol.NamedPipe
                && !string.Equals(accounts.PipeName, registration.PipeName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Account registration must target the same Accounts database endpoint as the AccountServer connection.");
        }

        if (string.IsNullOrWhiteSpace(registration.UserID)
            || string.Equals(registration.UserID, "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(registration.UserID, accounts.UserID, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Account registration requires a separate, non-root database identity.");
        }
    }
}
