using System.Globalization;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Registration;

/// <summary>A registration persistence failure containing only safe diagnostic fields.</summary>
public sealed class AccountRegistrationPersistenceException : Exception
{
    internal AccountRegistrationPersistenceException(bool commitOutcomeUnknown, int? databaseErrorNumber)
        : base((commitOutcomeUnknown
            ? "Account registration commit outcome is unknown. The insert was not retried."
            : "Account registration persistence failed.") +
            $" Database error number: {databaseErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}.")
    {
        CommitOutcomeUnknown = commitOutcomeUnknown;
        DatabaseErrorNumber = databaseErrorNumber;
    }

    public bool CommitOutcomeUnknown { get; }
    public int? DatabaseErrorNumber { get; }
}
