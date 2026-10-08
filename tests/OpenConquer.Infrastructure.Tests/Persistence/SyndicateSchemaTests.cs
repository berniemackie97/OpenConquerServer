using MySqlConnector;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class SyndicateSchemaTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 980_000;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RuntimeIdentity_CanReadSyndicateTablesButCannotWriteThem()
    {
        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        Assert.Equal(0L, await CountAsync(connection, "SELECT COUNT(*) FROM `syndicates` WHERE `syndicate_id` = 0"));
        Assert.Equal(0L, await CountAsync(connection, "SELECT COUNT(*) FROM `syndicate_memberships` WHERE `character_id` = 0"));

        await using MySqlCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO `syndicates` (`name`, `leader_character_id`, `silver_fund`, `emoney_fund`, `required_level`, `required_profession`, `required_metempsychosis`) VALUES ('Denied', 1000000, 0, 0, 0, 0, 0)";
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => insert.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(1142, exception.Number);

        await using MySqlCommand membershipInsert = connection.CreateCommand();
        membershipInsert.CommandText = "INSERT INTO `syndicate_memberships` (`character_id`, `syndicate_id`, `rank`, `proffer`, `position_expiration_unix_seconds`, `join_date_unix_seconds`) VALUES (1000000, 1, 0, 0, 0, 0)";
        MySqlException membershipException = await Assert.ThrowsAsync<MySqlException>(() => membershipInsert.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(1142, membershipException.Number);
    }

    [Fact]
    public async Task SyndicateIdentityAndName_AreUniqueAndNameCannotBeEmpty()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        uint leaderId = await InsertCharacterAsync(connection);
        uint otherLeaderId = await InsertCharacterAsync(connection);
        await InsertSyndicateAsync(connection, leaderId, "OpenConquer");

        MySqlException duplicateName = await Assert.ThrowsAsync<MySqlException>(() => InsertSyndicateAsync(connection, otherLeaderId, "OpenConquer"));
        Assert.Equal(1062, duplicateName.Number);

        MySqlException duplicateLeader = await Assert.ThrowsAsync<MySqlException>(() => InsertSyndicateAsync(connection, leaderId, "DifferentGuild"));
        Assert.Equal(1062, duplicateLeader.Number);

        MySqlException emptyName = await Assert.ThrowsAsync<MySqlException>(() => InsertSyndicateAsync(connection, otherLeaderId, ""));
        Assert.Equal(3819, emptyName.Number);
    }

    [Fact]
    public async Task Memberships_AreUniqueByCharacterAndReferencesAreRestrictive()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        uint leaderId = await InsertCharacterAsync(connection);
        uint memberId = await InsertCharacterAsync(connection);
        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, "Guild" + Guid.NewGuid().ToString("N")[..8]);
        await InsertMembershipAsync(connection, memberId, syndicateId);

        MySqlException duplicate = await Assert.ThrowsAsync<MySqlException>(() => InsertMembershipAsync(connection, memberId, syndicateId));
        Assert.Equal(1062, duplicate.Number);

        MySqlException missingSyndicate = await Assert.ThrowsAsync<MySqlException>(() => InsertMembershipAsync(connection, leaderId, ushort.MaxValue));
        Assert.Equal(1452, missingSyndicate.Number);

        await using MySqlCommand delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM `syndicates` WHERE `syndicate_id` = @syndicate_id";
        delete.Parameters.Add("@syndicate_id", MySqlDbType.UInt16).Value = syndicateId;
        MySqlException restricted = await Assert.ThrowsAsync<MySqlException>(() => delete.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(1451, restricted.Number);
    }

    [Fact]
    public async Task PopulationAndLeaderName_AreDerivedFromAuthoritativeRows()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        uint leaderId = await InsertCharacterAsync(connection);
        uint memberId = await InsertCharacterAsync(connection);
        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, "Guild" + Guid.NewGuid().ToString("N")[..8]);
        await InsertMembershipAsync(connection, leaderId, syndicateId);
        await InsertMembershipAsync(connection, memberId, syndicateId);

        await using MySqlCommand read = connection.CreateCommand();
        read.CommandText = """
            SELECT `c`.`name`, COUNT(`m`.`character_id`) AS `population`
            FROM `syndicates` AS `s`
            INNER JOIN `characters` AS `c` ON `c`.`character_id` = `s`.`leader_character_id`
            LEFT JOIN `syndicate_memberships` AS `m` ON `m`.`syndicate_id` = `s`.`syndicate_id`
            WHERE `s`.`syndicate_id` = @syndicate_id
            GROUP BY `s`.`syndicate_id`, `c`.`name`
            """;
        read.Parameters.Add("@syndicate_id", MySqlDbType.UInt16).Value = syndicateId;
        await using MySqlDataReader reader = await read.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.StartsWith("GuildChar", reader.GetString(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.False(await reader.ReadAsync(CancellationToken));

        Assert.True(CharacterIdentityPolicy.IsPlayerEntityId(leaderId));
    }

    [Fact]
    public async Task MembershipOpaqueUInt32Fields_RoundTripWithoutConversion()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        uint leaderId = await InsertCharacterAsync(connection);
        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, "Guild" + Guid.NewGuid().ToString("N")[..8]);
        await InsertMembershipAsync(connection, leaderId, syndicateId);

        await using MySqlCommand read = connection.CreateCommand();
        read.CommandText = """
            SELECT `rank`, `proffer`, `position_expiration_unix_seconds`, `join_date_unix_seconds`
            FROM `syndicate_memberships`
            WHERE `character_id` = @character_id
            """;
        read.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = leaderId;
        await using MySqlDataReader reader = await read.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(uint.MaxValue, reader.GetUInt32(0));
        Assert.Equal(uint.MaxValue, reader.GetUInt32(1));
        Assert.Equal(uint.MaxValue, reader.GetUInt32(2));
        Assert.Equal(uint.MaxValue, reader.GetUInt32(3));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task<uint> InsertCharacterAsync(MySqlConnection connection)
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "GuildChar" + Guid.NewGuid().ToString("N")[..6];
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters` (`account_id`, `name`, `appearance_composite`, `hair_composite`, `level`, `experience`,
                `strength`, `agility`, `vitality`, `spirit`, `unspent_attribute_points`, `current_life`, `current_mana`,
                `profession`, `first_profession`, `previous_profession`, `rebirth_count`, `pre_rebirth_level`, `silver`,
                `conquer_points`, `bound_conquer_points`, `pk_points`, `title_id`, `enlightenment_points`, `map_id`,
                `position_x`, `position_y`)
            VALUES (@account_id, @name, 1003, 410, 1, 0, 10, 10, 10, 10, 0, 100, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1002, 430, 378)
            """;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
        return checked((uint)command.LastInsertedId);
    }

    private static async Task<ushort> InsertSyndicateAsync(MySqlConnection connection, uint leaderId, string name)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `syndicates` (`name`, `leader_character_id`, `silver_fund`, `emoney_fund`,
                `required_level`, `required_profession`, `required_metempsychosis`)
            VALUES (@name, @leader_id, 0, 0, 0, 0, 0)
            """;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@leader_id", MySqlDbType.UInt32).Value = leaderId;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
        return checked((ushort)command.LastInsertedId);
    }

    private static async Task InsertMembershipAsync(MySqlConnection connection, uint characterId, ushort syndicateId)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `syndicate_memberships` (`character_id`, `syndicate_id`, `rank`, `proffer`,
                `position_expiration_unix_seconds`, `join_date_unix_seconds`)
            VALUES (@character_id, @syndicate_id, 4294967295, 4294967295, 4294967295, 4294967295)
            """;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@syndicate_id", MySqlDbType.UInt16).Value = syndicateId;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task<long> CountAsync(MySqlConnection connection, string sql)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken));
    }
}
