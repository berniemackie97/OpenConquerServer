using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.DependencyInjection;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Realms;

[Collection(AccountSchemaDatabaseCollection.Name)]
public sealed class MySqlRealmStatusTests(AccountDatabaseFixture database)
{
    private static readonly TimeSpan s_lifetime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private IRealmStatusDirectory Directory => database.Services.GetRequiredService<IRealmStatusDirectory>();

    [Fact]
    public async Task PublishAsync_ReplacesTheRealmReportReadByTheDirectory()
    {
        RealmId realm = await InsertRealmAsync(911, "StatusOne");
        await using ServiceProvider publisherServices = CreatePublisherServices(realm);
        IRealmStatusPublisher publisher = publisherServices.GetRequiredService<IRealmStatusPublisher>();

        Assert.Null(await Directory.FindCurrentLoadAsync(realm, CancellationToken));

        await publisher.PublishAsync(RealmLoad.Normal, 10, s_lifetime, CancellationToken);
        Assert.Equal(RealmLoad.Normal, await Directory.FindCurrentLoadAsync(realm, CancellationToken));

        await publisher.PublishAsync(RealmLoad.Full, 8192, s_lifetime, CancellationToken);
        Assert.Equal(RealmLoad.Full, await Directory.FindCurrentLoadAsync(realm, CancellationToken));
        Assert.Equal((RealmLoad.Full, 8192u), await ReadReportAsync(realm));
    }

    [Fact]
    public async Task WithdrawAsync_RemovesTheReport()
    {
        RealmId realm = await InsertRealmAsync(912, "StatusTwo");
        await using ServiceProvider publisherServices = CreatePublisherServices(realm);
        IRealmStatusPublisher publisher = publisherServices.GetRequiredService<IRealmStatusPublisher>();

        await publisher.PublishAsync(RealmLoad.Busy, 1, s_lifetime, CancellationToken);
        await publisher.WithdrawAsync(CancellationToken);

        Assert.Null(await Directory.FindCurrentLoadAsync(realm, CancellationToken));
    }

    [Fact]
    public async Task FindCurrentLoadAsync_IgnoresAnExpiredReport()
    {
        RealmId realm = await InsertRealmAsync(913, "StatusThree");
        await using ServiceProvider publisherServices = CreatePublisherServices(realm);

        await publisherServices.GetRequiredService<IRealmStatusPublisher>().PublishAsync(RealmLoad.Normal, 1, s_lifetime, CancellationToken);
        await ExecuteAdministrativeAsync($"""
            UPDATE `realm_status`
            SET `reported_at_utc` = TIMESTAMPADD(SECOND, -60, UTC_TIMESTAMP(6)),
                `expires_at_utc` = TIMESTAMPADD(SECOND, -30, UTC_TIMESTAMP(6))
            WHERE `realm_id` = {realm.Value};
            """);

        Assert.Null(await Directory.FindCurrentLoadAsync(realm, CancellationToken));
    }

    [Fact]
    public async Task RealmStatus_FollowsItsRealmAndRejectsInvalidState()
    {
        RealmId realm = await InsertRealmAsync(914, "StatusFour");
        await using ServiceProvider publisherServices = CreatePublisherServices(realm);
        await publisherServices.GetRequiredService<IRealmStatusPublisher>().PublishAsync(RealmLoad.Normal, 1, s_lifetime, CancellationToken);

        MySqlException invalidLoad = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAdministrativeAsync(
            $"UPDATE `realm_status` SET `load_state` = 4 WHERE `realm_id` = {realm.Value};"));
        MySqlException unknownRealm = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAdministrativeAsync("""
            INSERT INTO `realm_status` (`realm_id`, `load_state`, `online_count`, `reported_at_utc`, `expires_at_utc`)
            VALUES (999999, 1, 0, UTC_TIMESTAMP(6), TIMESTAMPADD(SECOND, 30, UTC_TIMESTAMP(6)));
            """));

        Assert.Equal(3819, invalidLoad.Number);
        Assert.Equal(1452, unknownRealm.Number);

        await ExecuteAdministrativeAsync($"DELETE FROM `realms` WHERE `realm_id` = {realm.Value};");

        Assert.Null(await ReadReportAsync(realm));
    }

    [Fact]
    public async Task PublishAsync_RejectsInvalidReports()
    {
        await using ServiceProvider publisherServices = CreatePublisherServices(new RealmId(1));
        IRealmStatusPublisher publisher = publisherServices.GetRequiredService<IRealmStatusPublisher>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync((RealmLoad)9, 0, s_lifetime, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync(RealmLoad.Normal, -1, s_lifetime, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync(RealmLoad.Normal, 0, TimeSpan.FromMilliseconds(999), CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync(RealmLoad.Normal, 0, TimeSpan.FromHours(2), CancellationToken).AsTask());
    }

    private ServiceProvider CreatePublisherServices(RealmId realm) =>
        new ServiceCollection().AddAccountPersistence(database.RuntimeConnectionString).AddGameServerRealmServices(realm).BuildServiceProvider();

    private async Task<RealmId> InsertRealmAsync(uint id, string name)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `realms` (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
            VALUES (@id, @name, @address, 5816, 1);
            """;
        command.Parameters.Add("@id", MySqlDbType.UInt32).Value = id;
        command.Parameters.Add("@name", MySqlDbType.VarChar, 15).Value = name;
        command.Parameters.Add("@address", MySqlDbType.Binary, 4).Value = IPAddress.Loopback.GetAddressBytes();
        await command.ExecuteNonQueryAsync(CancellationToken);

        return new RealmId(id);
    }

    private async Task<(RealmLoad Load, uint OnlineCount)?> ReadReportAsync(RealmId realm)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT `load_state`, `online_count` FROM `realm_status` WHERE `realm_id` = {realm.Value};";

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        return await reader.ReadAsync(CancellationToken) ? ((RealmLoad)reader.GetByte(0), reader.GetUInt32(1)) : null;
    }

    private async Task ExecuteAdministrativeAsync(string sql)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
