using MySqlConnector;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class GameSchemaIntegrityTests(GameDatabaseFixture database)
{
    private static readonly string[] s_expectedCheckConstraints =
    [
        "CK_characters_account_id",
        "CK_characters_appearance_composite",
        "CK_characters_level",
        "CK_characters_map_id",
        "CK_characters_name_length",
        "CK_characters_pre_rebirth_level",
        "CK_characters_profession",
        "CK_characters_rebirth_state",
        "CK_schema_compatibility_component_name",
        "CK_schema_compatibility_migration_id",
        "CK_schema_compatibility_schema_version",
    ];

    private static int s_nextAccountId = 1_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> CharacterConstraintCases =>
        new()
        {
            {
                "CK_characters_account_id",
                """
                    UPDATE `characters`
                    SET `account_id` = 0
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_appearance_composite",
                """
                    UPDATE `characters`
                    SET `appearance_composite` = 0
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_level",
                """
                    UPDATE `characters`
                    SET `level` = 0
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_pre_rebirth_level",
                $"""
                    UPDATE `characters`
                    SET `pre_rebirth_level` = {CharacterProgressionPolicy.MaximumLevel + 1}
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_rebirth_state",
                """
                    UPDATE `characters`
                    SET `pre_rebirth_level` = 130
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_rebirth_state",
                """
                    UPDATE `characters`
                    SET `rebirth_count` = 1
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_map_id",
                """
                    UPDATE `characters`
                    SET `map_id` = 0
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_name_length",
                """
                    UPDATE `characters`
                    SET `name` = 'abc'
                    WHERE `character_id` = @character_id
                    """
            },
            {
                "CK_characters_profession",
                """
                    UPDATE `characters`
                    SET `profession` = 0
                    WHERE `character_id` = @character_id
                    """
            },
        };

    public static TheoryData<string, string> SchemaCompatibilityConstraintCases =>
        new()
        {
            {
                "CK_schema_compatibility_component_name",
                """
                    UPDATE `schema_compatibility`
                    SET `component_name` = ''
                    WHERE `component_name` = 'game'
                    """
            },
            {
                "CK_schema_compatibility_schema_version",
                """
                    UPDATE `schema_compatibility`
                    SET `schema_version` = 0
                    WHERE `component_name` = 'game'
                    """
            },
            {
                "CK_schema_compatibility_migration_id",
                """
                    UPDATE `schema_compatibility`
                    SET `migration_id` = ''
                    WHERE `component_name` = 'game'
                    """
            },
        };

    [Fact]
    public async Task CheckConstraintMetadata_ContainsExpectedContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `CONSTRAINT_TYPE` = 'CHECK'
            ORDER BY `CONSTRAINT_NAME`
            """;

        List<string> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(s_expectedCheckConstraints.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CharacterIdentityAndNameColumns_UseExpectedStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `COLUMN_NAME`,
                `COLUMN_TYPE`,
                `IS_NULLABLE`,
                `CHARACTER_SET_NAME`,
                `COLLATION_NAME`,
                `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `COLUMN_NAME` IN ('character_id', 'account_id', 'name')
            ORDER BY `COLUMN_NAME`
            """;

        List<(string Name, string Type, string Nullable, string? CharacterSet, string? Collation, string Extra)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5)));
        }

        (string Name, string Type, string Nullable, string? CharacterSet, string? Collation, string Extra)[] expected =
        [
            ("account_id", "int unsigned", "NO", null, null, ""),
            ("character_id", "int unsigned", "NO", null, null, "auto_increment"),
            ("name", "varchar(15)", "NO", "utf8mb4", "utf8mb4_bin", ""),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CharacterPkPointsColumn_UsesSignedSmallintStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_TYPE`, `IS_NULLABLE`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `COLUMN_NAME` = 'pk_points'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("smallint", reader.GetString(0));
        Assert.Equal("NO", reader.GetString(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task CharacterPreRebirthLevelColumn_UsesExpectedStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_TYPE`, `IS_NULLABLE`, `COLUMN_DEFAULT`, `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `COLUMN_NAME` = 'pre_rebirth_level'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("tinyint unsigned", reader.GetString(0));
        Assert.Equal("NO", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal(string.Empty, reader.GetString(3));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task CharacterUniquenessIndexes_UseExpectedContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `INDEX_NAME`, `COLUMN_NAME`, `NON_UNIQUE`
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `INDEX_NAME` IN ('UX_characters_account_id', 'UX_characters_name')
            ORDER BY `INDEX_NAME`, `SEQ_IN_INDEX`
            """;

        List<(string IndexName, string ColumnName, bool Unique)> actual = [];
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((reader.GetString(0), reader.GetString(1), !reader.GetBoolean(2)));
        }

        (string IndexName, string ColumnName, bool Unique)[] expected =
        [
            ("UX_characters_account_id", "account_id", true),
            ("UX_characters_name", "name", true),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CharacterIdentitySequence_StartsAtFirstPlayerEntityId()
    {
        Assert.Equal((ulong)CharacterIdentityPolicy.FirstPlayerEntityId, database.InitialCharacterAutoIncrement);
    }

    [Fact]
    public async Task CharacterIdentityRange_RemainsInsidePlayerEntityRange()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE((SELECT MIN(`character_id`) FROM `characters`), 1000000),
                `AUTO_INCREMENT`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));

        ulong minimumCharacterId = reader.GetUInt64(0);
        ulong nextCharacterId = reader.GetUInt64(1);

        Assert.True(minimumCharacterId >= CharacterIdentityPolicy.FirstPlayerEntityId);
        Assert.True(nextCharacterId >= CharacterIdentityPolicy.FirstPlayerEntityId);
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task SchemaCompatibility_ContainsExpectedGameContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `component_name`, `schema_version`, `migration_id`
            FROM `schema_compatibility`
            WHERE `component_name` = 'game'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("game", reader.GetString(0));
        Assert.Equal(3u, reader.GetUInt32(1));
        Assert.Equal("20260923223920_RetainPreRebirthLevel", reader.GetString(2));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Theory]
    [MemberData(nameof(CharacterConstraintCases))]
    public async Task Characters_CheckConstraintsRejectInvalidState(string expectedConstraint, string commandText)
    {
        uint characterId = await InsertCharacterAsync(CreateAccountId(), CreateCharacterName());

        await AssertCheckConstraintViolationAsync(expectedConstraint, commandText, new MySqlParameter("@character_id", characterId));
    }

    [Theory]
    [MemberData(nameof(SchemaCompatibilityConstraintCases))]
    public async Task SchemaCompatibility_CheckConstraintsRejectInvalidState(string expectedConstraint, string commandText)
    {
        await AssertCheckConstraintViolationAsync(expectedConstraint, commandText);
    }

    [Fact]
    public async Task Characters_AccountOwnershipIsUnique()
    {
        uint accountId = CreateAccountId();

        await InsertCharacterAsync(accountId, CreateCharacterName());

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => InsertCharacterAsync(accountId, CreateCharacterName()));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("UX_characters_account_id", exception.Message);
    }

    [Fact]
    public async Task Characters_NameIsUnique()
    {
        string name = CreateCharacterName();

        await InsertCharacterAsync(CreateAccountId(), name);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => InsertCharacterAsync(CreateAccountId(), name));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("UX_characters_name", exception.Message);
    }

    private async Task<uint> InsertCharacterAsync(uint accountId, string name)
    {
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

        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@name", name);

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private async Task AssertCheckConstraintViolationAsync(string expectedConstraint, string commandText, params MySqlParameter[] parameters)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = new(commandText, connection);

        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(3819, exception.Number);
        Assert.Contains(expectedConstraint, exception.Message);
    }

    private static uint CreateAccountId()
    {
        return checked((uint)Interlocked.Increment(ref s_nextAccountId));
    }

    private static string CreateCharacterName()
    {
        return "Schema" + Guid.NewGuid().ToString("N")[..9];
    }
}
