using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Persistence.Game.Items;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterItemSetRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 200_000;
    private static readonly DateTimeOffset s_utcNow = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_ExistingCharacter_MapsCompletePersistedItemSet()
    {
        uint characterId = await InsertCharacterAsync();

        uint permanentItemId = await InsertItemAsync(characterId, itemTypeId: 100_000);
        uint pendingItemId = await InsertItemAsync(characterId, itemTypeId: 410_339, locationKind: 2, equipmentSet: 1, equipmentSlot: 4,
            lifetimeState: (byte)ItemLifetimeState.PendingActivation, lifetimeDurationSeconds: 3600);
        DateTime unlockAtUtc = new(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime expiresAtUtc = new(2027, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        uint activeItemId = await InsertItemAsync(characterId, itemTypeId: 120_249, locationKind: 2, equipmentSet: 2, equipmentSlot: 3,
            durability: 1200, maximumDurability: 1500, retailCompatibilityByteA: 1,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 123456, socket1Code: 13, socket2Code: 14,
            hiddenAttackEffect: 987654, retailCompatibilityByteB: 2, additionLevel: 12, damageReductionPercentOrSteedCompositionRed: 7,
            itemBindingCode: 3, enchantmentLifeBonusOrSteedCompositionGreen: 25, monsterRestraintIdOrSteedCompositionBlue: 456789,
            isSuspicious: true, equipmentLockStateMask: 0x8002, equipmentUnlockAtUtc: unlockAtUtc, equipmentColor: 5,
            compositionProgress: 12345, inscribedSyndicateId: 67890, stackQuantity: 20,
            lifetimeState: (byte)ItemLifetimeState.ActiveExpiry, lifetimeExpiresAtUtc: expiresAtUtc);

        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        CharacterItemSet itemSet = await repository.LoadAsync(characterId, s_utcNow, CancellationToken);

        Assert.Equal(characterId, itemSet.CharacterId);
        Assert.Equal(3, itemSet.Count);
        Assert.Equal([permanentItemId, pendingItemId, activeItemId], itemSet.Items.Select(static item => item.ItemId));

        CharacterItem permanent = itemSet.Items[0];
        Assert.True(permanent.Placement.IsInventory);
        Assert.Null(permanent.Placement.EquipmentPosition);
        Assert.Equal(ItemLifetime.CreatePermanent(), permanent.Lifetime);

        CharacterItem pending = itemSet.Items[1];
        Assert.True(pending.Placement.IsEquipment);
        Assert.Equal(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.RightHand), pending.Placement.EquipmentPosition);
        Assert.Equal(ItemLifetimeState.PendingActivation, pending.Lifetime.State);
        Assert.Equal(3600, pending.Lifetime.PendingActivationDurationSeconds);
        Assert.Null(pending.Lifetime.ExpiresAtUtc);

        CharacterItem active = itemSet.Items[2];
        Assert.Equal(activeItemId, active.ItemId);
        Assert.Equal(characterId, active.OwnerCharacterId);
        Assert.Equal(120_249u, active.ItemTypeId);
        Assert.True(active.Placement.IsEquipment);
        Assert.Equal(EquipmentPosition.Create(EquipmentSet.Alternate, EquipmentSlot.Armor), active.Placement.EquipmentPosition);
        Assert.Equal((ushort)1200, active.Durability);
        Assert.Equal((ushort)1500, active.MaximumDurability);
        Assert.Equal((byte)1, active.RetailCompatibilityByteA);
        Assert.Equal(123456u, active.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline);
        Assert.Equal((byte)13, active.Socket1Code);
        Assert.Equal((byte)14, active.Socket2Code);
        Assert.Equal(987654u, active.HiddenAttackEffect);
        Assert.Equal((byte)2, active.RetailCompatibilityByteB);
        Assert.Equal((byte)12, active.AdditionLevel);
        Assert.Equal((byte)7, active.DamageReductionPercentOrSteedCompositionRed);
        Assert.Equal((byte)3, active.ItemBindingCode);
        Assert.Equal((byte)25, active.EnchantmentLifeBonusOrSteedCompositionGreen);
        Assert.Equal(456789u, active.MonsterRestraintIdOrSteedCompositionBlue);
        Assert.True(active.IsSuspicious);
        Assert.Equal((ushort)0x8002, active.EquipmentLockStateMask);
        Assert.Equal(new DateTimeOffset(unlockAtUtc), active.EquipmentUnlockAtUtc);
        Assert.Equal((ushort)5, active.EquipmentColor);
        Assert.Equal(12345u, active.CompositionProgress);
        Assert.Equal(67890u, active.InscribedSyndicateId);
        Assert.Equal((ushort)20, active.StackQuantity);
        Assert.Equal(ItemLifetimeState.ActiveExpiry, active.Lifetime.State);
        Assert.Null(active.Lifetime.PendingActivationDurationSeconds);
        Assert.Equal(new DateTimeOffset(expiresAtUtc), active.Lifetime.ExpiresAtUtc);
    }

    [Fact]
    public async Task LoadAsync_CharacterWithoutItems_ReturnsEmptyItemSet()
    {
        uint characterId = await InsertCharacterAsync();
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        CharacterItemSet itemSet = await repository.LoadAsync(characterId, s_utcNow, CancellationToken);

        Assert.Equal(characterId, itemSet.CharacterId);
        Assert.Empty(itemSet.Items);
        Assert.Equal(0, itemSet.Count);
    }

    [Fact]
    public async Task LoadAsync_ItemCountAtConfiguredLimit_IsAccepted()
    {
        uint characterId = await InsertCharacterAsync();
        uint firstItemId = await InsertItemAsync(characterId);
        uint secondItemId = await InsertItemAsync(characterId);
        CharacterItemSetRepository repository = new(database.ContextFactory, new CharacterItemHydrationOptions(maximumItemsPerCharacter: 2));

        CharacterItemSet itemSet = await repository.LoadAsync(characterId, s_utcNow, CancellationToken);

        Assert.Equal(2, itemSet.Count);
        Assert.Equal([firstItemId, secondItemId], itemSet.Items.Select(static item => item.ItemId));
    }

    [Fact]
    public async Task LoadAsync_ItemCountAboveConfiguredLimit_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId);
        await InsertItemAsync(characterId);
        await InsertItemAsync(characterId);
        CharacterItemSetRepository repository = new(database.ContextFactory, new CharacterItemHydrationOptions(maximumItemsPerCharacter: 2));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(characterId, s_utcNow, CancellationToken));

        Assert.Contains(characterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("configured item hydration limit of 2", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public async Task LoadAsync_ExpiredRowsDoNotConsumeHydrationLimit()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddSeconds(-1).UtcDateTime);
        await InsertItemAsync(characterId, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.UtcDateTime);
        uint permanentItemId = await InsertItemAsync(characterId);
        uint futureItemId = await InsertItemAsync(characterId, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddSeconds(1).UtcDateTime);

        CharacterItemSetRepository repository = new(database.ContextFactory, new CharacterItemHydrationOptions(maximumItemsPerCharacter: 2));

        CharacterItemSet result = await repository.LoadAsync(characterId, s_utcNow, CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal([permanentItemId, futureItemId], result.Items.Select(static item => item.ItemId));
    }

    [Fact]
    public async Task LoadAsync_ExpiryCutoffDoesNotRoundForwardBeyondDatabasePrecision()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddTicks(10).UtcDateTime);

        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        CharacterItemSet result = await repository.LoadAsync(characterId, s_utcNow.AddTicks(9), CancellationToken);

        Assert.Equal(itemId, Assert.Single(result.Items).ItemId);
    }

    [Fact]
    public async Task LoadAsync_MalformedExpiredActiveLifetime_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, lifetimeState: (byte)ItemLifetimeState.ActiveExpiry,
            lifetimeExpiresAtUtc: s_utcNow.AddMinutes(-1).UtcDateTime);

        InvalidDataException exception = await AssertCorruptItemFailsClosedAsync(characterId, itemId,
            "`lifetime_duration_seconds` = 60", "CK_items_lifetime");

        Assert.Contains("invalid lifetime payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_NonUtcTimestamp_IsRejected()
    {
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId,
                s_utcNow.ToOffset(TimeSpan.FromHours(-4)), CancellationToken));

        Assert.Equal("utcNow", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task LoadAsync_NonPlayerCharacterId_IsRejected(uint characterId)
    {
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await repository.LoadAsync(characterId, s_utcNow, CancellationToken));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId, s_utcNow, cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_CorruptLocationKind_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        InvalidDataException exception = await AssertCorruptItemFailsClosedAsync(characterId, itemId, "`location_kind` = 3",
            "CK_items_location_kind", "CK_items_location_payload");

        Assert.Contains(itemId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_CorruptEquipmentPosition_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 10);

        InvalidDataException exception = await AssertCorruptItemFailsClosedAsync(characterId, itemId, "`equipment_set` = 2",
            "CK_items_alternate_equipment_slot");

        Assert.Contains(itemId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_CorruptLifetimePayload_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        InvalidDataException exception = await AssertCorruptItemFailsClosedAsync(characterId, itemId, "`lifetime_duration_seconds` = 60",
            "CK_items_lifetime");

        Assert.Contains(itemId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_CorruptUnlockSchedule_FailsClosed()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        InvalidDataException exception = await AssertCorruptItemFailsClosedAsync(characterId, itemId, "`equipment_lock_state_mask` = 2",
            "CK_items_equipment_unlock_schedule");

        Assert.Contains(itemId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task LoadAsync_DuplicateEquipmentPosition_FailsClosedAsPersistedCorruption()
    {
        uint characterId = await InsertCharacterAsync();
        uint firstItemId = await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 4);

        InvalidDataException exception = await AssertDuplicateEquipmentPositionFailsClosedAsync(characterId, firstItemId);

        Assert.Contains(characterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("invalid aggregate state", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task RuntimeIdentity_CanReadItemsButCannotWriteThem()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);
        ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();

        CharacterItemSet itemSet = await repository.LoadAsync(characterId, s_utcNow, CancellationToken);

        Assert.Single(itemSet.Items);
        Assert.Equal(itemId, itemSet.Items[0].ItemId);

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `items` WHERE `item_id` = @item_id";
        command.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = itemId;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1142, exception.Number);
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Hydrate" + Guid.NewGuid().ToString("N")[..8];

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

        return checked((uint)command.LastInsertedId);
    }

    private async Task<uint> InsertItemAsync(uint ownerCharacterId, uint itemTypeId = 100_000, byte locationKind = 1, byte? equipmentSet = null,
        byte? equipmentSlot = null, ushort durability = 100, ushort maximumDurability = 100, byte retailCompatibilityByteA = 0,
        uint talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline = 0, byte socket1Code = 0, byte socket2Code = 0,
        uint hiddenAttackEffect = 0, byte retailCompatibilityByteB = 0, byte additionLevel = 0,
        byte damageReductionPercentOrSteedCompositionRed = 0, byte itemBindingCode = 0, byte enchantmentLifeBonusOrSteedCompositionGreen = 0,
        uint monsterRestraintIdOrSteedCompositionBlue = 0, bool isSuspicious = false, ushort equipmentLockStateMask = 0,
        DateTime? equipmentUnlockAtUtc = null, ushort equipmentColor = 0, uint compositionProgress = 0, uint inscribedSyndicateId = 0,
        ushort stackQuantity = 1, byte lifetimeState = (byte)ItemLifetimeState.Permanent, int? lifetimeDurationSeconds = null,
        DateTime? lifetimeExpiresAtUtc = null)
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
                (@owner_character_id, @item_type_id, @location_kind, @equipment_set, @equipment_slot, @durability, @maximum_durability,
                 @retail_compatibility_byte_a, @socket_progress_or_steed_color_or_monster_counter_baseline, @socket1_code, @socket2_code,
                 @hidden_attack_effect, @retail_compatibility_byte_b, @addition_level, @damage_reduction_percent_or_steed_composition_red,
                 @item_binding_code, @enchantment_life_bonus_or_steed_composition_green, @monster_restraint_id_or_steed_composition_blue,
                 @is_suspicious, @equipment_lock_state_mask, @equipment_unlock_at_utc, @equipment_color, @composition_progress,
                 @inscribed_syndicate_id, @stack_quantity, @lifetime_state, @lifetime_duration_seconds, @lifetime_expires_at_utc)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@item_type_id", MySqlDbType.UInt32).Value = itemTypeId;
        command.Parameters.Add("@location_kind", MySqlDbType.UByte).Value = locationKind;
        command.Parameters.Add("@equipment_set", MySqlDbType.UByte).Value = equipmentSet is null ? DBNull.Value : equipmentSet.Value;
        command.Parameters.Add("@equipment_slot", MySqlDbType.UByte).Value = equipmentSlot is null ? DBNull.Value : equipmentSlot.Value;
        command.Parameters.Add("@durability", MySqlDbType.UInt16).Value = durability;
        command.Parameters.Add("@maximum_durability", MySqlDbType.UInt16).Value = maximumDurability;
        command.Parameters.Add("@retail_compatibility_byte_a", MySqlDbType.UByte).Value = retailCompatibilityByteA;
        command.Parameters.Add("@socket_progress_or_steed_color_or_monster_counter_baseline", MySqlDbType.UInt32).Value = talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline;
        command.Parameters.Add("@socket1_code", MySqlDbType.UByte).Value = socket1Code;
        command.Parameters.Add("@socket2_code", MySqlDbType.UByte).Value = socket2Code;
        command.Parameters.Add("@hidden_attack_effect", MySqlDbType.UInt32).Value = hiddenAttackEffect;
        command.Parameters.Add("@retail_compatibility_byte_b", MySqlDbType.UByte).Value = retailCompatibilityByteB;
        command.Parameters.Add("@addition_level", MySqlDbType.UByte).Value = additionLevel;
        command.Parameters.Add("@damage_reduction_percent_or_steed_composition_red", MySqlDbType.UByte).Value = damageReductionPercentOrSteedCompositionRed;
        command.Parameters.Add("@item_binding_code", MySqlDbType.UByte).Value = itemBindingCode;
        command.Parameters.Add("@enchantment_life_bonus_or_steed_composition_green", MySqlDbType.UByte).Value = enchantmentLifeBonusOrSteedCompositionGreen;
        command.Parameters.Add("@monster_restraint_id_or_steed_composition_blue", MySqlDbType.UInt32).Value = monsterRestraintIdOrSteedCompositionBlue;
        command.Parameters.Add("@is_suspicious", MySqlDbType.Byte).Value = isSuspicious;
        command.Parameters.Add("@equipment_lock_state_mask", MySqlDbType.UInt16).Value = equipmentLockStateMask;
        command.Parameters.Add("@equipment_unlock_at_utc", MySqlDbType.DateTime).Value = equipmentUnlockAtUtc is null ? DBNull.Value : equipmentUnlockAtUtc.Value;
        command.Parameters.Add("@equipment_color", MySqlDbType.UInt16).Value = equipmentColor;
        command.Parameters.Add("@composition_progress", MySqlDbType.UInt32).Value = compositionProgress;
        command.Parameters.Add("@inscribed_syndicate_id", MySqlDbType.UInt32).Value = inscribedSyndicateId;
        command.Parameters.Add("@stack_quantity", MySqlDbType.UInt16).Value = stackQuantity;
        command.Parameters.Add("@lifetime_state", MySqlDbType.UByte).Value = lifetimeState;
        command.Parameters.Add("@lifetime_duration_seconds", MySqlDbType.Int32).Value = lifetimeDurationSeconds is null ? DBNull.Value : lifetimeDurationSeconds.Value;
        command.Parameters.Add("@lifetime_expires_at_utc", MySqlDbType.DateTime).Value = lifetimeExpiresAtUtc is null ? DBNull.Value : lifetimeExpiresAtUtc.Value;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private async Task<InvalidDataException> AssertCorruptItemFailsClosedAsync(uint characterId, uint itemId, string assignment, params string[] constraintNames)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        List<string> disabledConstraints = [];

        try
        {
            foreach (string constraintName in constraintNames)
            {
                await SetCheckConstraintEnforcementAsync(connection, constraintName, enforced: false);
                disabledConstraints.Add(constraintName);
            }

            await using (MySqlCommand update = connection.CreateCommand())
            {
                update.CommandText = $"UPDATE `items` SET {assignment} WHERE `item_id` = @item_id";
                update.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = itemId;
                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken));
            }

            ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();
            return await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(characterId, s_utcNow, CancellationToken));
        }
        finally
        {
            await using (MySqlCommand delete = connection.CreateCommand())
            {
                delete.CommandText = "DELETE FROM `items` WHERE `item_id` = @item_id";
                delete.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = itemId;
                await delete.ExecuteNonQueryAsync(CancellationToken);
            }

            for (int index = disabledConstraints.Count - 1; index >= 0; index--)
            {
                await SetCheckConstraintEnforcementAsync(connection, disabledConstraints[index], enforced: true);
            }
        }
    }

    private async Task<InvalidDataException> AssertDuplicateEquipmentPositionFailsClosedAsync(uint characterId, uint firstItemId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        bool indexDropped = false;

        try
        {
            await using (MySqlCommand dropIndex = connection.CreateCommand())
            {
                dropIndex.CommandText = "ALTER TABLE `items` DROP INDEX `UX_items_owner_equipment_position`";
                await dropIndex.ExecuteNonQueryAsync(CancellationToken);
                indexDropped = true;
            }

            await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 4);

            ICharacterItemSetRepository repository = database.Services.GetRequiredService<ICharacterItemSetRepository>();
            return await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.LoadAsync(characterId, s_utcNow, CancellationToken));
        }
        finally
        {
            if (indexDropped)
            {
                await using (MySqlCommand deleteItems = connection.CreateCommand())
                {
                    deleteItems.CommandText = "DELETE FROM `items` WHERE `owner_character_id` = @character_id AND `equipment_set` = 1 AND `equipment_slot` = 4";
                    deleteItems.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;
                    await deleteItems.ExecuteNonQueryAsync(CancellationToken);
                }

                await using MySqlCommand createIndex = connection.CreateCommand();
                createIndex.CommandText = "CREATE UNIQUE INDEX `UX_items_owner_equipment_position` ON `items` (`owner_character_id`, `equipment_set`, `equipment_slot`)";
                await createIndex.ExecuteNonQueryAsync(CancellationToken);
            }
            else
            {
                await using MySqlCommand deleteItem = connection.CreateCommand();
                deleteItem.CommandText = "DELETE FROM `items` WHERE `item_id` = @item_id";
                deleteItem.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = firstItemId;
                await deleteItem.ExecuteNonQueryAsync(CancellationToken);
            }
        }
    }

    private static async Task SetCheckConstraintEnforcementAsync(MySqlConnection connection, string constraintName, bool enforced)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE `items` ALTER CHECK `{constraintName}` {(enforced ? "ENFORCED" : "NOT ENFORCED")}";
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
