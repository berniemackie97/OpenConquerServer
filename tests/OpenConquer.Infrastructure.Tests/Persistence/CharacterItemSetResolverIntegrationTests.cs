using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Persistence.Game.Items;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterItemSetResolverIntegrationTests(GameDatabaseFixture database)
{
    private const uint ItemTypeId = 100_000;
    private static int s_nextAccountId = 900_000;
    private static readonly DateTimeOffset s_utcNow = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolveAsync_ValidPersistedItem_PassesPersistenceAndCatalogBoundaries()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, ItemTypeId, stackQuantity: 20);
        CharacterItemSetResolver resolver = CreateResolver(CreateCatalog(stackCapacity: 20));

        CharacterItemSet result = await resolver.ResolveAsync(characterId, s_utcNow, CancellationToken);

        CharacterItem item = Assert.Single(result.Items);
        Assert.Equal(itemId, item.ItemId);
        Assert.Equal(ItemTypeId, item.ItemTypeId);
        Assert.Equal((ushort)20, item.StackQuantity);
    }

    [Fact]
    public async Task ResolveAsync_PersistedUnknownItemType_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, 999_999);
        CharacterItemSetResolver resolver = CreateResolver(CreateCatalog());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(characterId, s_utcNow, CancellationToken).AsTask());

        Assert.Contains("unknown item type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("999999", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_PersistedStackAboveCatalogCapacity_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, ItemTypeId, stackQuantity: 2);
        CharacterItemSetResolver resolver = CreateResolver(CreateCatalog(stackCapacity: 1));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(characterId, s_utcNow, CancellationToken).AsTask());

        Assert.Contains("stack quantity 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("capacity 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_PersistedPendingDurationMismatch_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, ItemTypeId, lifetimeState: (byte)ItemLifetimeState.PendingActivation, lifetimeDurationSeconds: 300);
        CharacterItemSetResolver resolver = CreateResolver(CreateCatalog(staticLifetimeMinutes: 10));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(characterId, s_utcNow, CancellationToken).AsTask());

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredPersistedUnknownItemType_IsRemovedBeforeCatalogValidation()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, 999_999, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddSeconds(-1).UtcDateTime);
        CharacterItemSetResolver resolver = CreateResolver(CreateCatalog());

        CharacterItemSet result = await resolver.ResolveAsync(characterId, s_utcNow, CancellationToken);

        Assert.Empty(result.Items);
    }


    [Fact]
    public async Task ResolveAsync_ExpiredRowsDoNotPreventBoundedCatalogResolution()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, 999_999, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddSeconds(-1).UtcDateTime);
        uint activeItemId = await InsertItemAsync(characterId, ItemTypeId);

        ICharacterItemSetRepository repository = new CharacterItemSetRepository(database.ContextFactory,
            new CharacterItemHydrationOptions(maximumItemsPerCharacter: 1));
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());

        CharacterItemSet result = await resolver.ResolveAsync(characterId, s_utcNow, CancellationToken);

        Assert.Equal(activeItemId, Assert.Single(result.Items).ItemId);
    }

    private CharacterItemSetResolver CreateResolver(ItemTypeCatalog itemTypes)
    {
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();
        return new CharacterItemSetResolver(repository, itemTypes);
    }

    private static ItemTypeCatalog CreateCatalog(uint staticLifetimeMinutes = 0, ushort stackCapacity = 1)
    {
        return new ItemTypeCatalog([new ItemTypeDefinition(ItemTypeId, $"Item{ItemTypeId}", 0, 0, 0, 0, 0, 0, staticLifetimeMinutes, stackCapacity)]);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Resolve" + Guid.NewGuid().ToString("N")[..8];

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

    private async Task<uint> InsertItemAsync(uint ownerCharacterId, uint itemTypeId, ushort stackQuantity = 1,
        byte lifetimeState = (byte)ItemLifetimeState.Permanent, int? lifetimeDurationSeconds = null, DateTime? lifetimeExpiresAtUtc = null)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO `items`
                (`owner_character_id`, `item_type_id`, `location_kind`, `equipment_set`, `equipment_slot`, `durability`, `maximum_durability`,
                 `retail_compatibility_byte_a`, `socket_progress_or_steed_color_or_monster_counter_baseline`, `socket1_code`, `socket2_code`,
                 `hidden_attack_effect`, `retail_compatibility_byte_b`, `addition_level`, `damage_reduction_percent_or_steed_composition_red`,
                 `item_binding_code`, `enchantment_life_bonus_or_steed_composition_green`, `monster_restraint_id_or_steed_composition_blue`,
                 `is_suspicious`, `equipment_lock_state_mask`, `equipment_unlock_at_utc`, `equipment_color`, `composition_progress`,
                 `inscribed_syndicate_id`, `stack_quantity`, `lifetime_state`, `lifetime_duration_seconds`, `lifetime_expires_at_utc`)
            VALUES
                (@owner_character_id, @item_type_id, 1, NULL, NULL, 100, 100,
                 0, 0, 0, 0,
                 0, 0, 0, 0,
                 0, 0, 0,
                 0, 0, NULL, 0, 0,
                 0, @stack_quantity, @lifetime_state, @lifetime_duration_seconds, @lifetime_expires_at_utc)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@item_type_id", MySqlDbType.UInt32).Value = itemTypeId;
        command.Parameters.Add("@stack_quantity", MySqlDbType.UInt16).Value = stackQuantity;
        command.Parameters.Add("@lifetime_state", MySqlDbType.UByte).Value = lifetimeState;
        command.Parameters.Add("@lifetime_duration_seconds", MySqlDbType.Int32).Value = lifetimeDurationSeconds is null ? DBNull.Value : lifetimeDurationSeconds.Value;
        command.Parameters.Add("@lifetime_expires_at_utc", MySqlDbType.DateTime).Value = lifetimeExpiresAtUtc is null ? DBNull.Value : lifetimeExpiresAtUtc.Value;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return checked((uint)command.LastInsertedId);
    }
}
