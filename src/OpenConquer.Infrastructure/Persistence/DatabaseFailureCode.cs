using System.Data.Common;
using System.Globalization;
using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence;

/// <summary>
/// Describes a database failure by its codes only. Driver messages can carry SQL parameter values, usernames or
/// connection details, so neither the message nor the exception is kept in diagnostics.
/// </summary>
public static class DatabaseFailureCode
{
    public static string Describe(DbException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string state = exception.SqlState ?? "unavailable";

        return exception is MySqlException mySql
            ? string.Create(CultureInfo.InvariantCulture, $"MySQL error {mySql.Number}, SQLSTATE {state}")
            : string.Create(CultureInfo.InvariantCulture, $"SQLSTATE {state}");
    }

    /// <summary>
    /// Returns an exception that is safe to log. When the failure was caused by a database exception anywhere in its
    /// chain, the whole chain is replaced by a code-only description; otherwise the exception is returned unchanged.
    /// </summary>
    public static Exception RedactForDiagnostics(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return FindDatabaseFailure(exception) is { } database
            ? new RedactedDatabaseFailureException(exception.GetType(), Describe(database))
            : exception;
    }

    private static DbException? FindDatabaseFailure(Exception exception)
    {
        switch (exception)
        {
            case DbException database:
                return database;
            case AggregateException aggregate:
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (FindDatabaseFailure(inner) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return exception.InnerException is { } next ? FindDatabaseFailure(next) : null;
        }
    }

    private sealed class RedactedDatabaseFailureException(Type failureType, string databaseFailure)
        : Exception($"{failureType.Name} caused by a database failure ({databaseFailure}). The driver message is withheld.");
}
