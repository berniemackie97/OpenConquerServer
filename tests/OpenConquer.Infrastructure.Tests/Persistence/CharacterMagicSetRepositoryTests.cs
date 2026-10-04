using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Infrastructure.Persistence.Game.Skills;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterMagicSetRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 700_000;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_ExistingCharacter_MapsPersistedMagicInDeterministicTypeOrder()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1300, ushort.MaxValue, uint.MaxValue);
        await InsertMagicAsync(characterId, 1000, 0, 0);
        await InsertMagicAsync(characterId, 1100, 4, 123456);

        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();

        CharacterMagicSet magicSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, magicSet.CharacterId);
        Assert.Equal(3, magicSet.Count);
        Assert.Equal([(ushort)1000, (ushort)1100, (ushort)1300], magicSet.Magic.Select(static magic => magic.Type));
        Assert.Equal((ushort)0, magicSet.Magic[0].Level);
        Assert.Equal(0u, magicSet.Magic[0].Experience);
        Assert.Equal((ushort)4, magicSet.Magic[1].Level);
        Assert.Equal(123456u, magicSet.Magic[1].Experience);
        Assert.Equal(ushort.MaxValue, magicSet.Magic[2].Level);
        Assert.Equal(uint.MaxValue, magicSet.Magic[2].Experience);
        Assert.All(magicSet.Magic, magic => Assert.Equal(characterId, magic.OwnerCharacterId));
    }

    [Fact]
    public async Task LoadAsync_CharacterWithoutMagic_ReturnsEmptySet()
    {
        uint characterId = await InsertCharacterAsync();
        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();

        CharacterMagicSet magicSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, magicSet.CharacterId);
        Assert.Empty(magicSet.Magic);
        Assert.Equal(0, magicSet.Count);
    }

    [Fact]
    public async Task LoadAsync_OtherCharactersMagicIsExcluded()
    {
        uint characterId = await InsertCharacterAsync();
        uint otherCharacterId = await InsertCharacterAsync();

        await InsertMagicAsync(otherCharacterId, 1000, 1, 100);

        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();

        CharacterMagicSet magicSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Empty(magicSet.Magic);
    }

    [Fact]
    public async Task LoadAsync_MagicCountAtConfiguredLimit_IsAccepted()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1000, 1, 0);
        await InsertMagicAsync(characterId, 1100, 1, 0);

        CharacterMagicSetRepository repository = new(database.ContextFactory, new CharacterMagicHydrationOptions(maximumMagicEntriesPerCharacter: 2));

        CharacterMagicSet magicSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(2, magicSet.Count);
    }

    [Fact]
    public async Task LoadAsync_MagicCountAboveConfiguredLimit_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1000, 1, 0);
        await InsertMagicAsync(characterId, 1100, 1, 0);
        await InsertMagicAsync(characterId, 1200, 1, 0);

        CharacterMagicSetRepository repository = new(database.ContextFactory, new CharacterMagicHydrationOptions(maximumMagicEntriesPerCharacter: 2));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(characterId, CancellationToken));

        Assert.Contains(characterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("configured magic hydration limit of 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task LoadAsync_NonPlayerCharacterId_IsRejected(uint characterId)
    {
        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await repository.LoadAsync(characterId, CancellationToken));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId, cancellation.Token));
    }

    [Fact]
    public async Task RuntimeIdentity_CanReadMagicButCannotWriteIt()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertMagicAsync(characterId, 1000, 1, 100);

        ICharacterMagicSetRepository repository = database.Services.GetRequiredService<ICharacterMagicSetRepository>();
        CharacterMagicSet magicSet = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Single(magicSet.Magic);

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `magic`
            WHERE `owner_character_id` = @owner_character_id
              AND `magic_type` = @magic_type
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@magic_type", MySqlDbType.UInt16).Value = (ushort)1000;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1142, exception.Number);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Magic" + Guid.NewGuid().ToString("N")[..10];

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
