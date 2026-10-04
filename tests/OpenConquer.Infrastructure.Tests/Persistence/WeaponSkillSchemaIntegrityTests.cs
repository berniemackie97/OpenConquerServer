using MySqlConnector;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class WeaponSkillSchemaIntegrityTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 600_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WeaponSkillColumns_UseExpectedStorageContract()
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
              AND `TABLE_NAME` = 'weapon_skills'
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
            ("weapon_skill_type", "int unsigned", "NO", null, ""),
            ("level", "tinyint unsigned", "NO", null, ""),
            ("experience", "int unsigned", "NO", null, ""),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task WeaponSkillTable_UsesExpectedCollation()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `TABLE_COLLATION`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'weapon_skills'
              AND `TABLE_TYPE` = 'BASE TABLE'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.Equal("utf8mb4_0900_as_cs", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task WeaponSkillPrimaryKey_UsesOwnerAndType()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_NAME`, `NON_UNIQUE`, `SEQ_IN_INDEX`
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'weapon_skills'
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
            ("weapon_skill_type", true, 2u),
        ], actual);
    }

    [Fact]
    public async Task WeaponSkillForeignKey_UsesRestrictContract()
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
              AND `kcu`.`TABLE_NAME` = 'weapon_skills'
              AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("FK_weapon_skills_characters_owner_character_id", reader.GetString(0));
        Assert.Equal("owner_character_id", reader.GetString(1));
        Assert.Equal("characters", reader.GetString(2));
        Assert.Equal("character_id", reader.GetString(3));
        Assert.Equal("RESTRICT", reader.GetString(4));
        Assert.Equal("RESTRICT", reader.GetString(5));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task WeaponSkillCheckConstraintMetadata_ContainsExpectedContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'weapon_skills'
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

        Assert.Equal(
        [
            "CK_weapon_skills_level",
            "CK_weapon_skills_owner_character_id",
        ], actual);
    }

    [Fact]
    public async Task WeaponSkills_DuplicateTypeForOwnerIsRejected()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 0);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertSkillAsync(characterId, 410, 2, 100));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("PRIMARY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeaponSkills_RejectUnknownOwner()
    {
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertSkillAsync(uint.MaxValue, 410, 1, 0));

        Assert.Equal(1452, exception.Number);
        Assert.Contains("FK_weapon_skills_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeaponSkills_LevelAboveMaximumIsRejected()
    {
        uint characterId = await InsertCharacterAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertSkillAsync(characterId, 410, WeaponSkillExperienceCurve.MaximumLevel + 1, 0));

        Assert.Equal(3819, exception.Number);
        Assert.Contains("CK_weapon_skills_level", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeaponSkills_FourDigitTypeIsPreserved()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 1050, 1, 0);

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `weapon_skill_type`
            FROM `weapon_skills`
            WHERE `owner_character_id` = @owner_character_id
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;

        Assert.Equal(1050u, Convert.ToUInt32(await command.ExecuteScalarAsync(CancellationToken)));
    }

    [Fact]
    public async Task Characters_WithWeaponSkillsCannotBeDeleted()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 0);

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `characters`
            WHERE `character_id` = @character_id
            """;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1451, exception.Number);
        Assert.Contains("FK_weapon_skills_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "WSkill" + Guid.NewGuid().ToString("N")[..9];

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

    private async Task InsertSkillAsync(uint ownerCharacterId, uint type, byte level, uint experience)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `weapon_skills`
                (`owner_character_id`, `weapon_skill_type`, `level`, `experience`)
            VALUES
                (@owner_character_id, @weapon_skill_type, @level, @experience)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@weapon_skill_type", MySqlDbType.UInt32).Value = type;
        command.Parameters.Add("@level", MySqlDbType.UByte).Value = level;
        command.Parameters.Add("@experience", MySqlDbType.UInt32).Value = experience;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }
}
