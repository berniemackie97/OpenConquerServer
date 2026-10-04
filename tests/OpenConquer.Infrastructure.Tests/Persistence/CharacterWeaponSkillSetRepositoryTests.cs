using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;
using OpenConquer.Infrastructure.Persistence.Game.Skills;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterWeaponSkillSetRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 500_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_ExistingCharacter_MapsPersistedSkillsInDeterministicTypeOrder()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 1050, 20, uint.MaxValue);
        await InsertSkillAsync(characterId, 500, 3, 250_000);
        await InsertSkillAsync(characterId, 410, 1, 1_200);

        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();

        CharacterWeaponSkillSet skillSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, skillSet.CharacterId);
        Assert.Equal(3, skillSet.Count);
        Assert.Equal([410u, 500u, 1050u], skillSet.Skills.Select(static skill => skill.Type));

        Assert.Equal((byte)1, skillSet.Skills[0].Level);
        Assert.Equal(1_200u, skillSet.Skills[0].Experience);
        Assert.Equal(1_200u, skillSet.Skills[0].NextLevelExperienceRequirement);

        Assert.Equal((byte)3, skillSet.Skills[1].Level);
        Assert.Equal(250_000u, skillSet.Skills[1].Experience);

        Assert.Equal((byte)20, skillSet.Skills[2].Level);
        Assert.Equal(uint.MaxValue, skillSet.Skills[2].Experience);
        Assert.Equal(0u, skillSet.Skills[2].NextLevelExperienceRequirement);
        Assert.All(skillSet.Skills, skill => Assert.Equal(characterId, skill.OwnerCharacterId));
    }

    [Fact]
    public async Task LoadAsync_CharacterWithoutSkills_ReturnsEmptySet()
    {
        uint characterId = await InsertCharacterAsync();
        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();

        CharacterWeaponSkillSet skillSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, skillSet.CharacterId);
        Assert.Empty(skillSet.Skills);
        Assert.Equal(0, skillSet.Count);
    }

    [Fact]
    public async Task LoadAsync_OtherCharactersSkillsAreExcluded()
    {
        uint characterId = await InsertCharacterAsync();
        uint otherCharacterId = await InsertCharacterAsync();

        await InsertSkillAsync(otherCharacterId, 410, 1, 100);

        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();

        CharacterWeaponSkillSet skillSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Empty(skillSet.Skills);
    }

    [Fact]
    public async Task LoadAsync_SkillCountAtConfiguredLimit_IsAccepted()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 0);
        await InsertSkillAsync(characterId, 420, 1, 0);

        CharacterWeaponSkillSetRepository repository = new(database.ContextFactory, new CharacterWeaponSkillHydrationOptions(maximumSkillsPerCharacter: 2));

        CharacterWeaponSkillSet skillSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(2, skillSet.Count);
    }

    [Fact]
    public async Task LoadAsync_SkillCountAboveConfiguredLimit_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 0);
        await InsertSkillAsync(characterId, 420, 1, 0);
        await InsertSkillAsync(characterId, 500, 1, 0);

        CharacterWeaponSkillSetRepository repository = new(database.ContextFactory, new CharacterWeaponSkillHydrationOptions(maximumSkillsPerCharacter: 2));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(characterId, CancellationToken));

        Assert.Contains(characterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("configured weapon-skill hydration limit of 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task LoadAsync_NonPlayerCharacterId_IsRejected(uint characterId)
    {
        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await repository.LoadAsync(characterId, CancellationToken));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId, cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_CorruptLevel_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 100);

        InvalidDataException exception = await AssertCorruptLevelFailsClosedAsync(characterId, 410);

        Assert.Contains(characterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("410", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task RuntimeIdentity_CanReadWeaponSkillsButCannotWriteThem()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertSkillAsync(characterId, 410, 1, 100);

        ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();

        CharacterWeaponSkillSet skillSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Single(skillSet.Skills);

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `weapon_skills`
            WHERE `owner_character_id` = @owner_character_id
              AND `weapon_skill_type` = @weapon_skill_type
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@weapon_skill_type", MySqlDbType.UInt32).Value = 410u;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1142, exception.Number);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Skill" + Guid.NewGuid().ToString("N")[..10];

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

    private async Task<InvalidDataException> AssertCorruptLevelFailsClosedAsync(uint ownerCharacterId, uint type)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        bool constraintDisabled = false;

        try
        {
            await SetCheckConstraintEnforcementAsync(connection, "weapon_skills", "CK_weapon_skills_level", enforced: false, CancellationToken);
            constraintDisabled = true;

            await using (MySqlCommand update = connection.CreateCommand())
            {
                update.CommandText = """
                    UPDATE `weapon_skills`
                    SET `level` = 21
                    WHERE `owner_character_id` = @owner_character_id
                      AND `weapon_skill_type` = @weapon_skill_type
                    """;
                update.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
                update.Parameters.Add("@weapon_skill_type", MySqlDbType.UInt32).Value = type;

                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken));
            }

            ICharacterWeaponSkillSetRepository repository = database.Services.GetRequiredService<ICharacterWeaponSkillSetRepository>();
            return await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(ownerCharacterId, CancellationToken));
        }
        finally
        {
            if (constraintDisabled)
            {
                CancellationToken cleanupToken = CancellationToken.None;

                try
                {
                    await using MySqlCommand restore = connection.CreateCommand();
                    restore.CommandText = """
                        UPDATE `weapon_skills`
                        SET `level` = 1
                        WHERE `owner_character_id` = @owner_character_id
                          AND `weapon_skill_type` = @weapon_skill_type
                        """;
                    restore.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
                    restore.Parameters.Add("@weapon_skill_type", MySqlDbType.UInt32).Value = type;

                    await restore.ExecuteNonQueryAsync(cleanupToken);
                }
                finally
                {
                    await SetCheckConstraintEnforcementAsync(connection, "weapon_skills", "CK_weapon_skills_level", enforced: true, cleanupToken);
                }
            }
        }
    }

    private static async Task SetCheckConstraintEnforcementAsync(MySqlConnection connection, string tableName, string constraintName, bool enforced, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE `{tableName}` ALTER CHECK `{constraintName}` {(enforced ? "ENFORCED" : "NOT ENFORCED")}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
