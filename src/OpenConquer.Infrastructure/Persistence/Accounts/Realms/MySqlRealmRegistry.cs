using System.Net;
using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Realms;

internal sealed class MySqlRealmRegistry(MySqlDataSource dataSource) : IRealmRegistry
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<RealmRegistration?> FindByNameAsync(ProtocolServerName protocolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(protocolName.Value))
        {
            throw new ArgumentException("Realm lookup requires a validated protocol name.", nameof(protocolName));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`
            FROM `realms`
            WHERE `protocol_server_name` = @protocol_server_name;
            """;
        command.Parameters.Add("@protocol_server_name", MySqlDbType.VarChar, ProtocolServerName.MaximumLength).Value = protocolName.Value;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        uint id = reader.GetUInt32(0);
        string storedName = reader.GetString(1);
        byte[] address = reader.GetFieldValue<byte[]>(2);
        ushort port = reader.GetUInt16(3);
        RealmAvailability availability = (RealmAvailability)reader.GetByte(4);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Realm registry has multiple entries for the same protocol name.");
        }

        try
        {
            if (!string.Equals(storedName, protocolName.Value, StringComparison.Ordinal) || address.Length != 4)
            {
                throw new InvalidDataException("Realm registry returned a non-canonical protocol name or IPv4 address.");
            }

            return new RealmRegistration(new RealmId(id), new ProtocolServerName(storedName), new IPAddress(address), port, availability);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Realm registry contains an invalid persisted registration.", exception);
        }
    }
}
