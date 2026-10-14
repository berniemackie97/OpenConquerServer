using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Realms;

internal sealed class MySqlRealmStatusDirectory(MySqlDataSource dataSource) : IRealmStatusDirectory
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<RealmLoad?> FindCurrentLoadAsync(RealmId realmId, CancellationToken cancellationToken = default)
    {
        if (realmId.Value == 0)
        {
            throw new ArgumentException("Realm status lookup requires a nonzero realm identity.", nameof(realmId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `load_state`
            FROM `realm_status`
            WHERE `realm_id` = @realm_id
              AND `expires_at_utc` > UTC_TIMESTAMP(6);
            """;
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = realmId.Value;

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        if (result is not byte value || !Enum.IsDefined((RealmLoad)value))
        {
            throw new InvalidDataException($"Realm {realmId.Value} has an invalid persisted load state.");
        }

        return (RealmLoad)value;
    }
}
