using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Persistence.Accounts;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Realms;

[Collection(AccountSchemaDatabaseCollection.Name)]
public sealed class MySqlRealmRegistryTests(AccountDatabaseFixture database)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private IRealmRegistry Registry => database.Services.GetRequiredService<IRealmRegistry>();

    [Fact]
    public async Task FindByNameAsync_PreservesIdentityAddressStateAndCaseSensitiveLookup()
    {
        await InsertRealmAsync(901, "RealmOne", "127.0.0.1", 5816, RealmAvailability.Enabled);
        await InsertRealmAsync(902, "realmone", "192.0.2.12", 5820, RealmAvailability.Maintenance);

        RealmRegistration first = Assert.IsType<RealmRegistration>(await Registry.FindByNameAsync(new ProtocolServerName("RealmOne"), CancellationToken));
        RealmRegistration second = Assert.IsType<RealmRegistration>(await Registry.FindByNameAsync(new ProtocolServerName("realmone"), CancellationToken));

        Assert.Equal(901u, first.Id.Value);
        Assert.Equal("RealmOne", first.ProtocolName.Value);
        Assert.Equal(IPAddress.Loopback, first.PublicGameAddress);
        Assert.Equal((ushort)5816, first.PublicGamePort);
        Assert.Equal(RealmAvailability.Enabled, first.Availability);
        Assert.Equal(902u, second.Id.Value);
        Assert.Equal(RealmAvailability.Maintenance, second.Availability);
        Assert.Equal(IPAddress.Parse("192.0.2.12"), second.PublicGameAddress);
        Assert.Null(await Registry.FindByNameAsync(new ProtocolServerName("MissingRealm"), CancellationToken));
    }

    [Fact]
    public async Task RealmTable_EnforcesUniquenessAndAvailabilityAndRuntimeReadOnlyPermissions()
    {
        await InsertRealmAsync(903, "RegistryTest", "127.0.0.1", 5817, RealmAvailability.Disabled);

        await Assert.ThrowsAsync<MySqlException>(() => InsertRealmAsync(904, "RegistryTest", "127.0.0.1", 5817, RealmAvailability.Enabled));
        await Assert.ThrowsAsync<MySqlException>(() => InsertRealmAsync(905, "Bad Realm", "127.0.0.1", 5817, RealmAvailability.Enabled));
        await Assert.ThrowsAsync<MySqlException>(() => InsertRealmAsync(906, "BadState", "127.0.0.1", 5817, (RealmAvailability)9));
        await Assert.ThrowsAsync<MySqlException>(() => InsertRealmAsync(907, "ZeroPort", "127.0.0.1", 0, RealmAvailability.Enabled));

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE `realms` SET `availability` = 1 WHERE `realm_id` = 903;";
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => update.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(1142, exception.Number);
    }

    [Fact]
    public async Task FindByNameAsync_PreCanceled_DoesNotRead()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Registry.FindByNameAsync(new ProtocolServerName("RealmOne"), cancellation.Token).AsTask());
    }

    private async Task InsertRealmAsync(uint id, string name, string address, ushort port, RealmAvailability state)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `realms` (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
            VALUES (@id, @name, @address, @port, @availability);
            """;
        command.Parameters.Add("@id", MySqlDbType.UInt32).Value = id;
        command.Parameters.Add("@name", MySqlDbType.VarChar, 15).Value = name;
        command.Parameters.Add("@address", MySqlDbType.Binary, 4).Value = IPAddress.Parse(address).GetAddressBytes();
        command.Parameters.Add("@port", MySqlDbType.UInt16).Value = port;
        command.Parameters.Add("@availability", MySqlDbType.UByte).Value = (byte)state;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
