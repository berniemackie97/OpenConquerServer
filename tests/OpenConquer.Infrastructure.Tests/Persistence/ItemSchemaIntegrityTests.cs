using MySqlConnector;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class ItemSchemaIntegrityTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 20_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ItemTable_UsesExpectedStorageContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `COLUMN_NAME`,
                `COLUMN_TYPE`,
                `IS_NULLABLE`,
                `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
            ORDER BY `ORDINAL_POSITION`
            """;

        List<(string Name, string Type, string Nullable, string Extra)> actual = [];

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        (string Name, string Type, string Nullable, string Extra)[] expected =
        [
            ("item_id", "int unsigned", "NO", "auto_increment"),
            ("owner_character_id", "int unsigned", "NO", ""),
            ("item_type_id", "int unsigned", "NO", ""),
            ("location_kind", "tinyint unsigned", "NO", ""),
            ("equipment_set", "tinyint unsigned", "YES", ""),
            ("equipment_slot", "tinyint unsigned", "YES", ""),
            ("durability", "smallint unsigned", "NO", ""),
            ("maximum_durability", "smallint unsigned", "NO", ""),
            ("retail_compatibility_byte_a", "tinyint unsigned", "NO", ""),
            ("talisman_socket_progress_or_steed_appearance_color_or_monster_kill_counter_baseline", "int unsigned", "NO", ""),
            ("socket1_code", "tinyint unsigned", "NO", ""),
            ("socket2_code", "tinyint unsigned", "NO", ""),
            ("hidden_attack_effect", "int unsigned", "NO", ""),
            ("retail_compatibility_byte_b", "tinyint unsigned", "NO", ""),
            ("addition_level", "tinyint unsigned", "NO", ""),
            ("damage_reduction_percent_or_steed_composition_red", "tinyint unsigned", "NO", ""),
            ("item_binding_code", "tinyint unsigned", "NO", ""),
            ("enchantment_life_bonus_or_steed_composition_green", "tinyint unsigned", "NO", ""),
            ("monster_restraint_id_or_steed_composition_blue", "int unsigned", "NO", ""),
            ("is_suspicious", "tinyint(1)", "NO", ""),
            ("equipment_lock_state_mask", "smallint unsigned", "NO", ""),
            ("equipment_unlock_at_utc", "datetime(6)", "YES", ""),
            ("equipment_color", "smallint unsigned", "NO", ""),
            ("composition_progress", "int unsigned", "NO", ""),
            ("inscribed_syndicate_id", "int unsigned", "NO", ""),
            ("stack_quantity", "smallint unsigned", "NO", ""),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ItemTable_UsesExpectedCollation()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `TABLE_COLLATION`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `TABLE_TYPE` = 'BASE TABLE'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.Equal("utf8mb4_0900_as_cs", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task ItemIndexes_UseExpectedContract()
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
              AND `TABLE_NAME` = 'items'
              AND `INDEX_NAME` IN
              (
                  'PRIMARY',
                  'IX_items_owner_character_id_location_kind',
                  'UX_items_owner_equipment_position'
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
            ("IX_items_owner_character_id_location_kind", "owner_character_id", false, 1),
            ("IX_items_owner_character_id_location_kind", "location_kind", false, 2),
            ("PRIMARY", "item_id", true, 1),
            ("UX_items_owner_equipment_position", "owner_character_id", true, 1),
            ("UX_items_owner_equipment_position", "equipment_set", true, 2),
            ("UX_items_owner_equipment_position", "equipment_slot", true, 3),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ItemOwnershipForeignKey_UsesRestrictContract()
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
              AND `kcu`.`TABLE_NAME` = 'items'
              AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("FK_items_characters_owner_character_id", reader.GetString(0));
        Assert.Equal("owner_character_id", reader.GetString(1));
        Assert.Equal("characters", reader.GetString(2));
        Assert.Equal("character_id", reader.GetString(3));
        Assert.Equal("RESTRICT", reader.GetString(4));
        Assert.Equal("RESTRICT", reader.GetString(5));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task ItemCheckConstraintMetadata_ContainsExpectedContract()
    {
        string[] expected =
        [
            "CK_items_alternate_equipment_slot",
            "CK_items_equipment_set",
            "CK_items_equipment_slot",
            "CK_items_equipment_unlock_schedule",
            "CK_items_is_suspicious",
            "CK_items_item_type_id",
            "CK_items_location_kind",
            "CK_items_location_payload",
            "CK_items_stack_quantity",
        ];

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
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
    public async Task Items_ValidInventoryRowsAllowMultipleItemsForSameOwner()
    {
        uint characterId = await InsertCharacterAsync();

        uint firstItemId = await InsertItemAsync(characterId);
        uint secondItemId = await InsertItemAsync(characterId);

        Assert.NotEqual(firstItemId, secondItemId);
    }

    [Fact]
    public async Task Items_AllSupportedEquipmentPositionsAreAccepted()
    {
        uint characterId = await InsertCharacterAsync();

        for (byte slot = 1; slot <= 16; slot++)
        {
            await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: slot);
        }

        for (byte slot = 1; slot <= 9; slot++)
        {
            await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 2, equipmentSlot: slot);
        }

        Assert.Equal(25L, await CountItemsForOwnerAsync(characterId));
    }

    [Fact]
    public async Task Items_RejectUnknownOwner()
    {
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => InsertItemAsync(uint.MaxValue));

        Assert.Equal(1452, exception.Number);
        Assert.Contains("FK_items_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Characters_WithOwnedItemsCannotBeDeleted()
    {
        uint characterId = await InsertCharacterAsync();
        await InsertItemAsync(characterId);

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
        Assert.Contains("FK_items_characters_owner_character_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Items_EquipmentPositionIsUniquePerOwner()
    {
        uint characterId = await InsertCharacterAsync();

        await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 4);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 4));

        Assert.Equal(1062, exception.Number);
        Assert.Contains("UX_items_owner_equipment_position", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Items_InvalidLocationKindIsRejected()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            ExecuteItemUpdateAsync(itemId, "`location_kind` = 3"));

        Assert.Equal(3819, exception.Number);
    }

    [Fact]
    public async Task Items_LocationPayloadMustMatchLocationKind()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_location_payload",
            "`equipment_set` = 1");
    }

    [Fact]
    public async Task Items_EquipmentSetMustBeCanonical()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 1);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_equipment_set",
            "`equipment_set` = 3");
    }

    [Fact]
    public async Task Items_EquipmentSlotMustBeCanonical()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 1);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_equipment_slot",
            "`equipment_slot` = 17");
    }

    [Fact]
    public async Task Items_AlternateEquipmentRejectsUnsupportedSlot()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId, locationKind: 2, equipmentSet: 1, equipmentSlot: 10);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_alternate_equipment_slot",
            "`equipment_set` = 2");
    }

    [Fact]
    public async Task Items_ItemTypeIdMustBeNonzero()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_item_type_id",
            "`item_type_id` = 0");
    }

    [Fact]
    public async Task Items_SuspicionFlagMustBeBoolean()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_is_suspicious",
            "`is_suspicious` = 2");
    }

    [Fact]
    public async Task Items_PendingUnlockRequiresUnlockSchedule()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_equipment_unlock_schedule",
            "`equipment_lock_state_mask` = 2");
    }

    [Fact]
    public async Task Items_UnlockScheduleRequiresPendingUnlockState()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_equipment_unlock_schedule",
            "`equipment_unlock_at_utc` = UTC_TIMESTAMP(6)");
    }

    [Fact]
    public async Task Items_StackQuantityMustBePositive()
    {
        uint characterId = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(characterId);

        await AssertItemCheckConstraintViolationAsync(
            itemId,
            "CK_items_stack_quantity",
            "`stack_quantity` = 0");
    }

    [Fact]
    public async Task Items_OwnershipCanTransferWithoutChangingIdentityOrLocation()
    {
        uint originalOwner = await InsertCharacterAsync();
        uint newOwner = await InsertCharacterAsync();
        uint itemId = await InsertItemAsync(originalOwner, locationKind: 2, equipmentSet: 1, equipmentSlot: 6);

        await ExecuteItemUpdateAsync(itemId, "`owner_character_id` = @new_owner",
            new MySqlParameter("@new_owner", MySqlDbType.UInt32) { Value = newOwner });

        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `item_id`,
                `owner_character_id`,
                `location_kind`,
                `equipment_set`,
                `equipment_slot`
            FROM `items`
            WHERE `item_id` = @item_id
            """;
        command.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = itemId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(itemId, reader.GetUInt32(0));
        Assert.Equal(newOwner, reader.GetUInt32(1));
        Assert.Equal((byte)2, reader.GetByte(2));
        Assert.Equal((byte)1, reader.GetByte(3));
        Assert.Equal((byte)6, reader.GetByte(4));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private async Task<uint> InsertCharacterAsync()
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Item" + Guid.NewGuid().ToString("N")[..11];

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

    private async Task<uint> InsertItemAsync(uint ownerCharacterId, byte locationKind = 1, byte? equipmentSet = null,
        byte? equipmentSlot = null, uint itemTypeId = 100000, ushort equipmentLockStateMask = 0,
        DateTime? equipmentUnlockAtUtc = null, byte isSuspicious = 0, ushort stackQuantity = 1)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `items`
                (`owner_character_id`,
                 `item_type_id`,
                 `location_kind`,
                 `equipment_set`,
                 `equipment_slot`,
                 `durability`,
                 `maximum_durability`,
                 `retail_compatibility_byte_a`,
                 `talisman_socket_progress_or_steed_appearance_color_or_monster_kill_counter_baseline`,
                 `socket1_code`,
                 `socket2_code`,
                 `hidden_attack_effect`,
                 `retail_compatibility_byte_b`,
                 `addition_level`,
                 `damage_reduction_percent_or_steed_composition_red`,
                 `item_binding_code`,
                 `enchantment_life_bonus_or_steed_composition_green`,
                 `monster_restraint_id_or_steed_composition_blue`,
                 `is_suspicious`,
                 `equipment_lock_state_mask`,
                 `equipment_unlock_at_utc`,
                 `equipment_color`,
                 `composition_progress`,
                 `inscribed_syndicate_id`,
                 `stack_quantity`)
            VALUES
                (@owner_character_id,
                 @item_type_id,
                 @location_kind,
                 @equipment_set,
                 @equipment_slot,
                 100,
                 100,
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
                 0,
                 @is_suspicious,
                 @equipment_lock_state_mask,
                 @equipment_unlock_at_utc,
                 0,
                 0,
                 0,
                 @stack_quantity)
            """;

        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@item_type_id", MySqlDbType.UInt32).Value = itemTypeId;
        command.Parameters.Add("@location_kind", MySqlDbType.UByte).Value = locationKind;
        command.Parameters.Add("@equipment_set", MySqlDbType.UByte).Value = equipmentSet is null ? DBNull.Value : equipmentSet.Value;
        command.Parameters.Add("@equipment_slot", MySqlDbType.UByte).Value = equipmentSlot is null ? DBNull.Value : equipmentSlot.Value;
        command.Parameters.Add("@is_suspicious", MySqlDbType.UByte).Value = isSuspicious;
        command.Parameters.Add("@equipment_lock_state_mask", MySqlDbType.UInt16).Value = equipmentLockStateMask;
        command.Parameters.Add("@equipment_unlock_at_utc", MySqlDbType.DateTime).Value = equipmentUnlockAtUtc is null ? DBNull.Value : equipmentUnlockAtUtc.Value;
        command.Parameters.Add("@stack_quantity", MySqlDbType.UInt16).Value = stackQuantity;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private async Task<long> CountItemsForOwnerAsync(uint characterId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `items`
            WHERE `owner_character_id` = @owner_character_id
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);

        return Convert.ToInt64(result);
    }

    private async Task AssertItemCheckConstraintViolationAsync(uint itemId, string expectedConstraint, string assignment)
    {
        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => ExecuteItemUpdateAsync(itemId, assignment));

        Assert.Equal(3819, exception.Number);
        Assert.Contains(expectedConstraint, exception.Message, StringComparison.Ordinal);
    }

    private async Task ExecuteItemUpdateAsync(uint itemId, string assignment, params MySqlParameter[] parameters)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE `items`
            SET {assignment}
            WHERE `item_id` = @item_id
            """;
        command.Parameters.Add("@item_id", MySqlDbType.UInt32).Value = itemId;

        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
