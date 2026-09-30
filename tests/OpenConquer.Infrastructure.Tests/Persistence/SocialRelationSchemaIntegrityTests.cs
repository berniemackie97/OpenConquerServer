using MySqlConnector;
using OpenConquer.Domain.Social;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class SocialRelationSchemaIntegrityTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 400_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SocialRelationColumns_UseExpectedStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `COLUMN_NAME`,
                `COLUMN_TYPE`,
                `IS_NULLABLE`,
                `COLUMN_DEFAULT`,
                `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
            ORDER BY `ORDINAL_POSITION`
            """;

        List<(string Name, string Type, string Nullable, object? Default, string Extra)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetValue(3),
                reader.GetString(4)));
        }

        (string Name, string Type, string Nullable, object? Default, string Extra)[] expected =
        [
            ("owner_character_id", "int unsigned", "NO", null, ""),
            ("counterpart_character_id", "int unsigned", "NO", null, ""),
            ("kind", "tinyint unsigned", "NO", null, ""),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SocialRelationTable_UsesExpectedCollation()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `TABLE_COLLATION`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
              AND `TABLE_TYPE` = 'BASE TABLE'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.Equal("utf8mb4_0900_as_cs", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task SocialRelationIndexes_UseExpectedContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `INDEX_NAME`,
                `COLUMN_NAME`,
                `NON_UNIQUE`,
                `SEQ_IN_INDEX`
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
              AND `INDEX_NAME` IN
              (
                  'PRIMARY',
                  'IX_social_relations_counterpart_kind_owner'
              )
            ORDER BY `INDEX_NAME`, `SEQ_IN_INDEX`
            """;

        List<(string IndexName, string ColumnName, bool Unique, uint Sequence)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((reader.GetString(0), reader.GetString(1), !reader.GetBoolean(2), reader.GetUInt32(3)));
        }

        (string IndexName, string ColumnName, bool Unique, uint Sequence)[] expected =
        [
            ("IX_social_relations_counterpart_kind_owner", "counterpart_character_id", false, 1),
            ("IX_social_relations_counterpart_kind_owner", "kind", false, 2),
            ("IX_social_relations_counterpart_kind_owner", "owner_character_id", false, 3),
            ("PRIMARY", "owner_character_id", true, 1),
            ("PRIMARY", "kind", true, 2),
            ("PRIMARY", "counterpart_character_id", true, 3),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SocialRelationForeignKeys_UseRestrictContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `kcu`.`CONSTRAINT_NAME`,
                `kcu`.`COLUMN_NAME`,
                `kcu`.`REFERENCED_TABLE_NAME`,
                `kcu`.`REFERENCED_COLUMN_NAME`,
                `rc`.`DELETE_RULE`,
                `rc`.`UPDATE_RULE`
            FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
            INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
            WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
              AND `kcu`.`TABLE_NAME` = 'social_relations'
              AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            ORDER BY `kcu`.`CONSTRAINT_NAME`
            """;

        List<(string Constraint, string Column, string ReferencedTable, string ReferencedColumn, string DeleteRule, string UpdateRule)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        (string Constraint, string Column, string ReferencedTable, string ReferencedColumn, string DeleteRule, string UpdateRule)[] expected =
        [
            ("FK_social_relations_characters_counterpart_character_id", "counterpart_character_id", "characters", "character_id", "RESTRICT", "RESTRICT"),
            ("FK_social_relations_characters_owner_character_id", "owner_character_id", "characters", "character_id", "RESTRICT", "RESTRICT"),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SocialRelationCheckConstraintMetadata_ContainsExpectedContract()
    {
        string[] expected =
        [
            "CK_social_relations_counterpart_character_id",
            "CK_social_relations_distinct_characters",
            "CK_social_relations_kind",
            "CK_social_relations_owner_character_id",
        ];

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
              AND `CONSTRAINT_TYPE` = 'CHECK'
              AND `ENFORCED` = 'YES'
            ORDER BY `CONSTRAINT_NAME`
            """;

        List<string> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SocialRelations_FriendAndEnemyMayCoexistForSameCounterpart()
    {
        uint ownerCharacterId = await InsertCharacterAsync();
        uint counterpartCharacterId = await InsertCharacterAsync();

        await InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Friend);
        await InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Enemy);

        Assert.Equal(2L, await CountRelationsAsync(ownerCharacterId, counterpartCharacterId));
    }

    [Fact]
    public async Task SocialRelations_ReciprocalFriendRowsMayCoexist()
    {
        uint firstCharacterId = await InsertCharacterAsync();
        uint secondCharacterId = await InsertCharacterAsync();

        await InsertRelationAsync(firstCharacterId, secondCharacterId, (byte)SocialRelationKind.Friend);
        await InsertRelationAsync(secondCharacterId, firstCharacterId, (byte)SocialRelationKind.Friend);

        Assert.Equal(1L, await CountRelationsAsync(firstCharacterId, secondCharacterId));
        Assert.Equal(1L, await CountRelationsAsync(secondCharacterId, firstCharacterId));
    }

    [Fact]
    public async Task SocialRelations_DuplicateRelationIsRejected()
    {
        uint ownerCharacterId = await InsertCharacterAsync();
        uint counterpartCharacterId = await InsertCharacterAsync();

        await InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Friend);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Friend));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("PRIMARY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SocialRelations_RejectUnknownOwner()
    {
        uint counterpartCharacterId = await InsertCharacterAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertRelationAsync(uint.MaxValue, counterpartCharacterId, (byte)SocialRelationKind.Friend));

        Assert.Equal(1452, exception.Number);
        Assert.Contains("FK_social_relations_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SocialRelations_RejectUnknownCounterpart()
    {
        uint ownerCharacterId = await InsertCharacterAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertRelationAsync(ownerCharacterId, uint.MaxValue, (byte)SocialRelationKind.Friend));

        Assert.Equal(1452, exception.Number);
        Assert.Contains("FK_social_relations_characters_counterpart_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SocialRelations_RejectSelfRelation()
    {
        uint characterId = await InsertCharacterAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertRelationAsync(characterId, characterId, (byte)SocialRelationKind.Friend));

        Assert.Equal(3819, exception.Number);
        Assert.Contains("CK_social_relations_distinct_characters", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(byte.MaxValue)]
    public async Task SocialRelations_KindMustBeCanonical(byte kind)
    {
        uint ownerCharacterId = await InsertCharacterAsync();
        uint counterpartCharacterId = await InsertCharacterAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertRelationAsync(ownerCharacterId, counterpartCharacterId, kind));

        Assert.Equal(3819, exception.Number);
        Assert.Contains("CK_social_relations_kind", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Characters_WithOutgoingSocialRelationsCannotBeDeleted()
    {
        uint ownerCharacterId = await InsertCharacterAsync();
        uint counterpartCharacterId = await InsertCharacterAsync();

        await InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Friend);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => DeleteCharacterAsync(ownerCharacterId));

        Assert.Equal(1451, exception.Number);
        Assert.Contains("FK_social_relations_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Characters_WithIncomingSocialRelationsCannotBeDeleted()
    {
        uint ownerCharacterId = await InsertCharacterAsync();
        uint counterpartCharacterId = await InsertCharacterAsync();

        await InsertRelationAsync(ownerCharacterId, counterpartCharacterId, (byte)SocialRelationKind.Friend);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => DeleteCharacterAsync(counterpartCharacterId));

        Assert.Equal(1451, exception.Number);
        Assert.Contains("FK_social_relations_characters_counterpart_character_id", exception.Message, StringComparison.Ordinal);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Social" + Guid.NewGuid().ToString("N")[..9];

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters`
                (`account_id`,
                 `name`,
                 `appearance_composite`,
                 `hair_composite`,
                 `level`,
                 `experience`,
                 `strength`,
                 `agility`,
                 `vitality`,
                 `spirit`,
                 `unspent_attribute_points`,
                 `current_life`,
                 `current_mana`,
                 `profession`,
                 `first_profession`,
                 `previous_profession`,
                 `rebirth_count`,
                 `pre_rebirth_level`,
                 `silver`,
                 `conquer_points`,
                 `bound_conquer_points`,
                 `pk_points`,
                 `title_id`,
                 `enlightenment_points`,
                 `map_id`,
                 `position_x`,
                 `position_y`)
            VALUES
                (@account_id,
                 @name,
                 1003,
                 410,
                 1,
                 0,
                 10,
                 10,
                 10,
                 10,
                 0,
                 100,
                 0,
                 10,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 1002,
                 430,
                 378)
            """;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private async Task InsertRelationAsync(uint ownerCharacterId, uint counterpartCharacterId, byte kind)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `social_relations`
                (`owner_character_id`,
                 `counterpart_character_id`,
                 `kind`)
            VALUES
                (@owner_character_id,
                 @counterpart_character_id,
                 @kind)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;
        command.Parameters.Add("@kind", MySqlDbType.UByte).Value = kind;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task<long> CountRelationsAsync(uint ownerCharacterId, uint counterpartCharacterId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `social_relations`
            WHERE `owner_character_id` = @owner_character_id
              AND `counterpart_character_id` = @counterpart_character_id
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);

        return Convert.ToInt64(result);
    }

    private async Task DeleteCharacterAsync(uint characterId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `characters`
            WHERE `character_id` = @character_id
            """;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
