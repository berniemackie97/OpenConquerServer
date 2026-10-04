using MySqlConnector;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class MagicSchemaIntegrityTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 800_000;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MagicColumns_UseExpectedStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_NAME`, `COLUMN_TYPE`, `IS_NULLABLE`, `COLUMN_DEFAULT`, `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'magic'
            ORDER BY `ORDINAL_POSITION`
            """;

        List<(string Name, string Type, string Nullable, object? Default, string Extra)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetValue(3), reader.GetString(4)));
        }

        (string Name, string Type, string Nullable, object? Default, string Extra)[] expected =
        [
            ("owner_character_id", "int unsigned", "NO", null, ""),
            ("magic_type", "smallint unsigned", "NO", null, ""),
            ("level", "smallint unsigned", "NO", null, ""),
            ("experience", "int unsigned", "NO", null, ""),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MagicTable_UsesExpectedCollation()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `TABLE_COLLATION`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'magic'
              AND `TABLE_TYPE` = 'BASE TABLE'
            """;

        Assert.Equal("utf8mb4_0900_as_cs", Assert.IsType<string>(await command.ExecuteScalarAsync(CancellationToken)));
    }

    [Fact]
    public async Task MagicPrimaryKey_UsesOwnerAndType()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_NAME`, `NON_UNIQUE`, `SEQ_IN_INDEX`
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'magic'
              AND `INDEX_NAME` = 'PRIMARY'
            ORDER BY `SEQ_IN_INDEX`
            """;

        List<(string Column, bool Unique, uint Sequence)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((reader.GetString(0), !reader.GetBoolean(1), reader.GetUInt32(2)));
        }

        Assert.Equal(
        [
            ("owner_character_id", true, 1u),
            ("magic_type", true, 2u),
        ], actual);
    }

    [Fact]
    public async Task MagicForeignKey_UsesRestrictContract()
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
              AND `kcu`.`TABLE_NAME` = 'magic'
              AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("FK_magic_characters_owner_character_id", reader.GetString(0));
        Assert.Equal("owner_character_id", reader.GetString(1));
        Assert.Equal("characters", reader.GetString(2));
        Assert.Equal("character_id", reader.GetString(3));
        Assert.Equal("RESTRICT", reader.GetString(4));
        Assert.Equal("RESTRICT", reader.GetString(5));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task MagicCheckConstraintMetadata_ContainsOnlyOwnerContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'magic'
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

        Assert.Equal(["CK_magic_owner_character_id"], actual);
    }

    [Fact]
    public async Task Magic_DuplicateTypeForOwnerIsRejected()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1000, 1, 0);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => InsertMagicAsync(characterId, 1000, 2, 100));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("PRIMARY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Magic_RejectsUnknownOwner()
    {
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => InsertMagicAsync(uint.MaxValue, 1000, 1, 0));

        Assert.Equal(1452, exception.Number);
        Assert.Contains("FK_magic_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Magic_FullWireRangesArePreserved()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 0, ushort.MaxValue, uint.MaxValue);
        await InsertMagicAsync(characterId, ushort.MaxValue, 0, 0);

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `magic_type`, `level`, `experience`
            FROM `magic`
            WHERE `owner_character_id` = @owner_character_id
            ORDER BY `magic_type`
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal((ushort)0, reader.GetUInt16(0));
        Assert.Equal(ushort.MaxValue, reader.GetUInt16(1));
        Assert.Equal(uint.MaxValue, reader.GetUInt32(2));

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(ushort.MaxValue, reader.GetUInt16(0));
        Assert.Equal((ushort)0, reader.GetUInt16(1));
        Assert.Equal(0u, reader.GetUInt32(2));

        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task Characters_WithMagicCannotBeDeleted()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1000, 1, 0);

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `characters` WHERE `character_id` = @character_id";
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1451, exception.Number);
        Assert.Contains("FK_magic_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "MagicDb" + Guid.NewGuid().ToString("N")[..8];

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters`
                (`account_id`, `name`, `appearance_composite`, `hair_composite`, `level`, `experience`, `strength`, `agility`, `vitality`, `spirit`,
                 `unspent_attribute_points`, `current_life`, `current_mana`, `profession`, `first_profession`, `previous_profession`, `rebirth_count`,
                 `pre_rebirth_level`, `silver`, `conquer_points`, `bound_conquer_points`, `pk_points`, `title_id`, `enlightenment_points`, `map_id`,
                 `position_x`, `position_y`)
            VALUES
                (@account_id, @name, 1003, 410, 1, 0, 10, 10, 10, 10, 0, 100, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1002, 430, 378)
            """;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return checked((uint)command.LastInsertedId);
    }

    private async Task InsertMagicAsync(uint ownerCharacterId, ushort type, ushort level, uint experience)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `magic`
                (`owner_character_id`, `magic_type`, `level`, `experience`)
            VALUES
                (@owner_character_id, @magic_type, @level, @experience)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@magic_type", MySqlDbType.UInt16).Value = type;
        command.Parameters.Add("@level", MySqlDbType.UInt16).Value = level;
        command.Parameters.Add("@experience", MySqlDbType.UInt32).Value = experience;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }
}
