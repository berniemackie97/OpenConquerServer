using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence;

internal static class AccountDbContextOptionsConfiguration
{
    private static readonly MySqlServerVersion s_serverVersion = new(new Version(8, 4, 0));

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        MySqlConnectionStringBuilder connection = new(connectionString)
        {
            UseAffectedRows = false,
            GuidFormat = MySqlGuidFormat.Binary16,
            DateTimeKind = MySqlDateTimeKind.Utc,
        };

        options.UseMySql(connection.ConnectionString, s_serverVersion, mysql => mysql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null));
    }
}
