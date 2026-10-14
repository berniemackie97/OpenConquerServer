using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.DependencyInjection;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Sessions;

[Collection(AccountSchemaDatabaseCollection.Name)]
public sealed class MySqlAccountSessionStoreTests(AccountDatabaseFixture database)
{
    private const uint CharacterId = 1_000_001;
    private const uint SessionUid = 0x1020_3040;

    private static readonly TimeSpan s_statusLifetime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private IAccountSessionKickRequester KickRequester => database.Services.GetRequiredService<IAccountSessionKickRequester>();

    [Fact]
    public async Task TryEnterAsync_RecordsTheAccountInTheRealm()
    {
        await using RealmServices realm = await CreateRealmAsync(921, "SessionOne", reporting: true);
        uint account = await InsertAccountAsync();

        Assert.True(await realm.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));

        Assert.Equal((realm.Id.Value, CharacterId, SessionUid), await ReadSessionAsync(account));
    }

    [Fact]
    public async Task TryEnterAsync_LoginAgainInTheSameRealm_ReplacesTheRecord()
    {
        await using RealmServices realm = await CreateRealmAsync(922, "SessionTwo", reporting: true);
        uint account = await InsertAccountAsync();

        Assert.True(await realm.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));
        Assert.True(await realm.Sessions.TryEnterAsync(account, CharacterId, SessionUid + 1, CancellationToken));

        await realm.Sessions.LeaveAsync(account, SessionUid, CancellationToken);

        Assert.Equal((realm.Id.Value, CharacterId, SessionUid + 1), await ReadSessionAsync(account));

        await realm.Sessions.LeaveAsync(account, SessionUid + 1, CancellationToken);

        Assert.Null(await ReadSessionAsync(account));
        Assert.Empty(await realm.Sessions.TakeKicksAsync(CancellationToken));
    }

    [Fact]
    public async Task TryEnterAsync_AccountPlayingInAnotherRealm_IsRefusedAndThatRealmIsAskedToDisconnectIt()
    {
        await using RealmServices playing = await CreateRealmAsync(923, "SessionThree", reporting: true);
        await using RealmServices entering = await CreateRealmAsync(924, "SessionFour", reporting: true);
        uint account = await InsertAccountAsync();

        Assert.True(await playing.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));
        Assert.False(await entering.Sessions.TryEnterAsync(account, CharacterId + 1, SessionUid + 1, CancellationToken));

        Assert.Equal((playing.Id.Value, CharacterId, SessionUid), await ReadSessionAsync(account));
        Assert.Empty(await entering.Sessions.TakeKicksAsync(CancellationToken));
        Assert.Equal([new AccountSessionKick(account, CharacterId, SessionUid)], await playing.Sessions.TakeKicksAsync(CancellationToken));
        Assert.Empty(await playing.Sessions.TakeKicksAsync(CancellationToken));

        await playing.Sessions.LeaveAsync(account, SessionUid, CancellationToken);

        Assert.True(await entering.Sessions.TryEnterAsync(account, CharacterId + 1, SessionUid + 1, CancellationToken));
        Assert.Equal((entering.Id.Value, CharacterId + 1, SessionUid + 1), await ReadSessionAsync(account));
    }

    [Fact]
    public async Task TryEnterAsync_RecordLeftInARealmThatStoppedReporting_IsTakenOver()
    {
        await using RealmServices stopped = await CreateRealmAsync(925, "SessionFive", reporting: false);
        await using RealmServices entering = await CreateRealmAsync(926, "SessionSix", reporting: true);
        uint account = await InsertAccountAsync();

        Assert.True(await stopped.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));
        Assert.True(await entering.Sessions.TryEnterAsync(account, CharacterId, SessionUid + 1, CancellationToken));

        Assert.Equal((entering.Id.Value, CharacterId, SessionUid + 1), await ReadSessionAsync(account));
        Assert.Empty(await stopped.Sessions.TakeKicksAsync(CancellationToken));
    }

    [Fact]
    public async Task TakeKicksAsync_ReportsAKickedAccountThatAlreadyLeft()
    {
        await using RealmServices playing = await CreateRealmAsync(927, "SessionSeven", reporting: true);
        await using RealmServices entering = await CreateRealmAsync(928, "SessionEight", reporting: true);
        uint account = await InsertAccountAsync();

        Assert.True(await playing.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));
        Assert.False(await entering.Sessions.TryEnterAsync(account, CharacterId, SessionUid + 1, CancellationToken));
        await playing.Sessions.LeaveAsync(account, SessionUid, CancellationToken);

        Assert.Equal([new AccountSessionKick(account, CharacterId: null, SessionUid: null)], await playing.Sessions.TakeKicksAsync(CancellationToken));
    }

    [Fact]
    public async Task ResetAsync_ClearsOnlyTheRealmsOwnRecordsAndKicks()
    {
        await using RealmServices restarted = await CreateRealmAsync(929, "SessionNine", reporting: true);
        await using RealmServices other = await CreateRealmAsync(930, "SessionTen", reporting: true);
        uint restartedAccount = await InsertAccountAsync();
        uint otherAccount = await InsertAccountAsync();

        Assert.True(await restarted.Sessions.TryEnterAsync(restartedAccount, CharacterId, SessionUid, CancellationToken));
        Assert.True(await other.Sessions.TryEnterAsync(otherAccount, CharacterId, SessionUid, CancellationToken));
        Assert.False(await other.Sessions.TryEnterAsync(restartedAccount, CharacterId, SessionUid + 1, CancellationToken));

        await restarted.Sessions.ResetAsync(CancellationToken);

        Assert.Null(await ReadSessionAsync(restartedAccount));
        Assert.Empty(await restarted.Sessions.TakeKicksAsync(CancellationToken));
        Assert.Equal((other.Id.Value, CharacterId, SessionUid), await ReadSessionAsync(otherAccount));
    }

    [Fact]
    public async Task TryKickFromOtherRealmAsync_AsksOnlyAReportingRealmOtherThanTheSelectedOne()
    {
        await using RealmServices playing = await CreateRealmAsync(931, "SessionKickA", reporting: true);
        await using RealmServices stopped = await CreateRealmAsync(932, "SessionKickB", reporting: false);
        uint playingAccount = await InsertAccountAsync();
        uint strandedAccount = await InsertAccountAsync();
        uint offlineAccount = await InsertAccountAsync();

        Assert.True(await playing.Sessions.TryEnterAsync(playingAccount, CharacterId, SessionUid, CancellationToken));
        Assert.True(await stopped.Sessions.TryEnterAsync(strandedAccount, CharacterId, SessionUid, CancellationToken));

        Assert.False(await KickRequester.TryKickFromOtherRealmAsync(playingAccount, playing.Id, CancellationToken));
        Assert.False(await KickRequester.TryKickFromOtherRealmAsync(strandedAccount, playing.Id, CancellationToken));
        Assert.False(await KickRequester.TryKickFromOtherRealmAsync(offlineAccount, playing.Id, CancellationToken));
        Assert.Empty(await playing.Sessions.TakeKicksAsync(CancellationToken));
        Assert.Empty(await stopped.Sessions.TakeKicksAsync(CancellationToken));

        Assert.True(await KickRequester.TryKickFromOtherRealmAsync(playingAccount, stopped.Id, CancellationToken));
        Assert.True(await KickRequester.TryKickFromOtherRealmAsync(playingAccount, stopped.Id, CancellationToken));

        Assert.Equal([new AccountSessionKick(playingAccount, CharacterId, SessionUid)], await playing.Sessions.TakeKicksAsync(CancellationToken));
    }

    [Fact]
    public async Task AccountSessions_FollowTheirAccountAndRealmAndRejectInvalidState()
    {
        await using RealmServices realm = await CreateRealmAsync(933, "SessionRules", reporting: true);
        uint account = await InsertAccountAsync();
        uint removedAccount = await InsertAccountAsync();

        Assert.True(await realm.Sessions.TryEnterAsync(account, CharacterId, SessionUid, CancellationToken));
        Assert.True(await realm.Sessions.TryEnterAsync(removedAccount, CharacterId, SessionUid, CancellationToken));

        MySqlException invalidCharacter = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAdministrativeAsync(
            $"UPDATE `account_sessions` SET `character_id` = 0 WHERE `account_id` = {account};"));
        MySqlException invalidSession = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAdministrativeAsync(
            $"UPDATE `account_sessions` SET `session_uid` = 0 WHERE `account_id` = {account};"));
        MySqlException unknownRealm = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAdministrativeAsync(
            $"UPDATE `account_sessions` SET `realm_id` = 999999 WHERE `account_id` = {account};"));

        Assert.Equal(3819, invalidCharacter.Number);
        Assert.Equal(3819, invalidSession.Number);
        Assert.Equal(1452, unknownRealm.Number);

        await ExecuteAdministrativeAsync($"DELETE FROM `accounts` WHERE `account_id` = {removedAccount};");

        Assert.Null(await ReadSessionAsync(removedAccount));

        await ExecuteAdministrativeAsync($"DELETE FROM `realms` WHERE `realm_id` = {realm.Id.Value};");

        Assert.Null(await ReadSessionAsync(account));
    }

    [Fact]
    public async Task Store_RejectsZeroIdentities()
    {
        await using RealmServices realm = await CreateRealmAsync(934, "SessionZero", reporting: false);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => realm.Sessions.TryEnterAsync(0, CharacterId, SessionUid, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => realm.Sessions.TryEnterAsync(1, 0, SessionUid, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => realm.Sessions.TryEnterAsync(1, CharacterId, 0, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => realm.Sessions.LeaveAsync(0, SessionUid, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => realm.Sessions.LeaveAsync(1, 0, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => KickRequester.TryKickFromOtherRealmAsync(0, realm.Id, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => KickRequester.TryKickFromOtherRealmAsync(1, default, CancellationToken).AsTask());
    }

    private async Task<RealmServices> CreateRealmAsync(uint id, string name, bool reporting)
    {
        await using (MySqlConnection connection = new(database.AdministrativeConnectionString))
        {
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
        }

        RealmId realmId = new(id);
        ServiceProvider services = new ServiceCollection()
            .AddAccountPersistence(database.RuntimeConnectionString)
            .AddGameServerRealmServices(realmId)
            .BuildServiceProvider();

        if (reporting)
        {
            await services.GetRequiredService<IRealmStatusPublisher>().PublishAsync(RealmLoad.Normal, 0, s_statusLifetime, CancellationToken);
        }

        return new RealmServices(realmId, services);
    }

    private async Task<uint> InsertAccountAsync()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `accounts`
                (`username`, `access_status`, `authority_role`, `creation_operation_id`, `last_successful_login_at_utc`,
                 `state_revision`, `created_at_utc`, `created_by_actor_kind`, `created_by_account_id`, `state_changed_at_utc`,
                 `state_changed_by_actor_kind`, `state_changed_by_account_id`, `deleted_at_utc`, `deleted_by_actor_kind`, `deleted_by_account_id`)
            VALUES
                (@username, 1, 1, UUID_TO_BIN(UUID()), NULL,
                 1, '2026-01-01 00:00:00', 3, NULL, '2026-01-01 00:00:00',
                 3, NULL, NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("@username", "Session" + Guid.NewGuid().ToString("N")[..25]);
        await command.ExecuteNonQueryAsync(CancellationToken);

        return checked((uint)command.LastInsertedId);
    }

    private async Task<(uint RealmId, uint CharacterId, uint SessionUid)?> ReadSessionAsync(uint accountId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT `realm_id`, `character_id`, `session_uid` FROM `account_sessions` WHERE `account_id` = {accountId};";

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        return await reader.ReadAsync(CancellationToken) ? (reader.GetUInt32(0), reader.GetUInt32(1), reader.GetUInt32(2)) : null;
    }

    private async Task ExecuteAdministrativeAsync(string sql)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private sealed class RealmServices(RealmId id, ServiceProvider services) : IAsyncDisposable
    {
        public RealmId Id { get; } = id;
        public IAccountSessionStore Sessions { get; } = services.GetRequiredService<IAccountSessionStore>();

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
}
