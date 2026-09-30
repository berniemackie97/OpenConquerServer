using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;
using OpenConquer.Infrastructure.Persistence.Game.Social;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterSocialRelationSetRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 300_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_ExistingCharacter_MapsAuthoritativePersistedRelationsInDeterministicOrder()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint firstCounterpartId, string firstCounterpartName) = await InsertCharacterAsync();
        (uint secondCounterpartId, _) = await InsertCharacterAsync();
        (uint thirdCounterpartId, string thirdCounterpartName) = await InsertCharacterAsync();
        string renamedSecondCounterpart = "Renamed" + Guid.NewGuid().ToString("N")[..8];

        await InsertRelationAsync(ownerId, secondCounterpartId, SocialRelationKind.Enemy);
        await InsertRelationAsync(ownerId, thirdCounterpartId, SocialRelationKind.Friend);
        await InsertRelationAsync(ownerId, firstCounterpartId, SocialRelationKind.Enemy);
        await InsertRelationAsync(ownerId, firstCounterpartId, SocialRelationKind.Friend);
        await UpdateCharacterNameAsync(secondCounterpartId, renamedSecondCounterpart);

        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();

        CharacterSocialRelationSet relationSet = await repository.LoadAsync(ownerId, CancellationToken);

        Assert.Equal(ownerId, relationSet.CharacterId);
        Assert.Equal(4, relationSet.Count);

        Assert.Equal(
            [
                (SocialRelationKind.Friend, firstCounterpartId),
                (SocialRelationKind.Friend, thirdCounterpartId),
                (SocialRelationKind.Enemy, firstCounterpartId),
                (SocialRelationKind.Enemy, secondCounterpartId),
            ],
            relationSet.Relations.Select(static relation => (relation.Kind, relation.CounterpartCharacterId)));

        Assert.Equal(firstCounterpartName, relationSet.Relations[0].CounterpartName);
        Assert.Equal(thirdCounterpartName, relationSet.Relations[1].CounterpartName);
        Assert.Equal(firstCounterpartName, relationSet.Relations[2].CounterpartName);
        Assert.Equal(renamedSecondCounterpart, relationSet.Relations[3].CounterpartName);
        Assert.All(relationSet.Relations, relation => Assert.Equal(ownerId, relation.OwnerCharacterId));
    }

    [Fact]
    public async Task LoadAsync_CharacterWithoutRelations_ReturnsEmptyRelationSet()
    {
        (uint characterId, _) = await InsertCharacterAsync();
        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();

        CharacterSocialRelationSet relationSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, relationSet.CharacterId);
        Assert.Empty(relationSet.Relations);
        Assert.Equal(0, relationSet.Count);
    }

    [Fact]
    public async Task LoadAsync_IncomingRelationIsNotIncludedInOwnerSet()
    {
        (uint characterId, _) = await InsertCharacterAsync();
        (uint otherCharacterId, _) = await InsertCharacterAsync();

        await InsertRelationAsync(otherCharacterId, characterId, SocialRelationKind.Friend);

        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();

        CharacterSocialRelationSet relationSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Empty(relationSet.Relations);
    }

    [Fact]
    public async Task LoadAsync_RelationCountAtConfiguredLimit_IsAccepted()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint firstCounterpartId, _) = await InsertCharacterAsync();
        (uint secondCounterpartId, _) = await InsertCharacterAsync();

        await InsertRelationAsync(ownerId, firstCounterpartId, SocialRelationKind.Friend);
        await InsertRelationAsync(ownerId, secondCounterpartId, SocialRelationKind.Enemy);

        CharacterSocialRelationSetRepository repository = new(database.ContextFactory, new CharacterSocialRelationHydrationOptions(maximumRelationsPerCharacter: 2));

        CharacterSocialRelationSet relationSet = await repository.LoadAsync(ownerId, CancellationToken);

        Assert.Equal(2, relationSet.Count);
    }

    [Fact]
    public async Task LoadAsync_RelationCountAboveConfiguredLimit_FailsClosed()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint firstCounterpartId, _) = await InsertCharacterAsync();
        (uint secondCounterpartId, _) = await InsertCharacterAsync();
        (uint thirdCounterpartId, _) = await InsertCharacterAsync();

        await InsertRelationAsync(ownerId, firstCounterpartId, SocialRelationKind.Friend);
        await InsertRelationAsync(ownerId, secondCounterpartId, SocialRelationKind.Friend);
        await InsertRelationAsync(ownerId, thirdCounterpartId, SocialRelationKind.Enemy);

        CharacterSocialRelationSetRepository repository = new(database.ContextFactory, new CharacterSocialRelationHydrationOptions(maximumRelationsPerCharacter: 2));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(ownerId, CancellationToken));

        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("configured social-relation hydration limit of 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task LoadAsync_NonPlayerCharacterId_IsRejected(uint characterId)
    {
        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await repository.LoadAsync(characterId, CancellationToken));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId, cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_CorruptRelationKind_FailsClosed()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint counterpartId, _) = await InsertCharacterAsync();

        await InsertRelationAsync(ownerId, counterpartId, SocialRelationKind.Friend);

        InvalidDataException exception = await AssertCorruptRelationKindFailsClosedAsync(ownerId, counterpartId);

        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(counterpartId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task LoadAsync_CorruptCounterpartName_FailsClosed()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint counterpartId, string counterpartName) = await InsertCharacterAsync();

        await InsertRelationAsync(ownerId, counterpartId, SocialRelationKind.Friend);

        InvalidDataException exception = await AssertCorruptCounterpartNameFailsClosedAsync(ownerId, counterpartId, counterpartName);

        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(counterpartId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task RuntimeIdentity_CanReadSocialRelationsButCannotWriteThem()
    {
        (uint ownerId, _) = await InsertCharacterAsync();
        (uint counterpartId, _) = await InsertCharacterAsync();

        await InsertRelationAsync(ownerId, counterpartId, SocialRelationKind.Friend);

        ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();

        CharacterSocialRelationSet relationSet = await repository.LoadAsync(ownerId, CancellationToken);

        Assert.Single(relationSet.Relations);
        Assert.Equal(counterpartId, relationSet.Relations[0].CounterpartCharacterId);

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `social_relations`
            WHERE `owner_character_id` = @owner_character_id
              AND `counterpart_character_id` = @counterpart_character_id
              AND `kind` = @kind
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerId;
        command.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartId;
        command.Parameters.Add("@kind", MySqlDbType.UByte).Value = (byte)SocialRelationKind.Friend;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1142, exception.Number);
    }

    private async Task<(uint CharacterId, string Name)> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Rel" + Guid.NewGuid().ToString("N")[..12];

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

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return (checked((uint)command.LastInsertedId), name);
    }

    private async Task InsertRelationAsync(uint ownerCharacterId, uint counterpartCharacterId, SocialRelationKind kind)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `social_relations`
                (`owner_character_id`, `counterpart_character_id`, `kind`)
            VALUES
                (@owner_character_id, @counterpart_character_id, @kind)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;
        command.Parameters.Add("@kind", MySqlDbType.UByte).Value = (byte)kind;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task UpdateCharacterNameAsync(uint characterId, string name)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE `characters`
            SET `name` = @name
            WHERE `character_id` = @character_id
            """;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private async Task<InvalidDataException> AssertCorruptRelationKindFailsClosedAsync(uint ownerCharacterId, uint counterpartCharacterId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        bool constraintDisabled = false;

        try
        {
            await SetCheckConstraintEnforcementAsync(connection, "social_relations", "CK_social_relations_kind", enforced: false);
            constraintDisabled = true;

            await using (MySqlCommand update = connection.CreateCommand())
            {
                update.CommandText = """
                    UPDATE `social_relations`
                    SET `kind` = 3
                    WHERE `owner_character_id` = @owner_character_id
                      AND `counterpart_character_id` = @counterpart_character_id
                      AND `kind` = 1
                    """;
                update.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
                update.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;

                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken));
            }

            ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();
            return await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(ownerCharacterId, CancellationToken));
        }
        finally
        {
            if (constraintDisabled)
            {
                await using (MySqlCommand restore = connection.CreateCommand())
                {
                    restore.CommandText = """
                        UPDATE `social_relations`
                        SET `kind` = 1
                        WHERE `owner_character_id` = @owner_character_id
                          AND `counterpart_character_id` = @counterpart_character_id
                          AND `kind` = 3
                        """;
                    restore.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
                    restore.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;
                    await restore.ExecuteNonQueryAsync(CancellationToken);
                }

                await SetCheckConstraintEnforcementAsync(connection, "social_relations", "CK_social_relations_kind", enforced: true);
            }
        }
    }

    private async Task<InvalidDataException> AssertCorruptCounterpartNameFailsClosedAsync(uint ownerCharacterId, uint counterpartCharacterId, string originalName)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        bool constraintDisabled = false;

        try
        {
            await SetCheckConstraintEnforcementAsync(connection, "characters", "CK_characters_name_length", enforced: false);
            constraintDisabled = true;

            await using (MySqlCommand update = connection.CreateCommand())
            {
                update.CommandText = """
                    UPDATE `characters`
                    SET `name` = 'abc'
                    WHERE `character_id` = @character_id
                    """;
                update.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;

                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken));
            }

            ICharacterSocialRelationSetRepository repository = database.Services.GetRequiredService<ICharacterSocialRelationSetRepository>();
            return await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(ownerCharacterId, CancellationToken));
        }
        finally
        {
            if (constraintDisabled)
            {
                await using (MySqlCommand restore = connection.CreateCommand())
                {
                    restore.CommandText = """
                        UPDATE `characters`
                        SET `name` = @name
                        WHERE `character_id` = @character_id
                        """;
                    restore.Parameters.Add("@name", MySqlDbType.VarChar).Value = originalName;
                    restore.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;
                    await restore.ExecuteNonQueryAsync(CancellationToken);
                }

                await SetCheckConstraintEnforcementAsync(connection, "characters", "CK_characters_name_length", enforced: true);
            }
        }
    }

    private static async Task SetCheckConstraintEnforcementAsync(MySqlConnection connection, string tableName, string constraintName, bool enforced)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE `{tableName}` ALTER CHECK `{constraintName}` {(enforced ? "ENFORCED" : "NOT ENFORCED")}";
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
