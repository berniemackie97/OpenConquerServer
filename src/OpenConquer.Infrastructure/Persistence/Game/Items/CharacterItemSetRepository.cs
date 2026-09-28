using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Items;

public sealed class CharacterItemSetRepository(IDbContextFactory<GameDbContext> contextFactory, CharacterItemHydrationOptions options) : ICharacterItemSetRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly int _maximumItemsPerCharacter = (options ?? throw new ArgumentNullException(nameof(options))).MaximumItemsPerCharacter;

    public async ValueTask<CharacterItemSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"An item-set lookup character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<ItemRecord> records = await db.Items.AsNoTracking().Where(item => item.OwnerCharacterId == characterId)
            .OrderBy(item => item.ItemId).Take(_maximumItemsPerCharacter + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (records.Count > _maximumItemsPerCharacter)
        {
            throw new InvalidDataException($"Character {characterId} exceeds the configured item hydration limit of {_maximumItemsPerCharacter} items.");
        }

        CharacterItem[] items = new CharacterItem[records.Count];

        for (int index = 0; index < records.Count; index++)
        {
            items[index] = CreateItem(records[index]);
        }

        try
        {
            return new CharacterItemSet(characterId, items);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted item set for character {characterId} contains invalid aggregate state.", exception);
        }
    }

    private static CharacterItem CreateItem(ItemRecord record)
    {
        try
        {
            return new CharacterItem(record.ItemId, record.OwnerCharacterId, record.ItemTypeId, CreatePlacement(record),
                record.Durability, record.MaximumDurability, record.RetailCompatibilityByteA,
                record.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline, record.Socket1Code, record.Socket2Code,
                record.HiddenAttackEffect, record.RetailCompatibilityByteB, record.AdditionLevel, record.DamageReductionPercentOrSteedCompositionRed,
                record.ItemBindingCode, record.EnchantmentLifeBonusOrSteedCompositionGreen, record.MonsterRestraintIdOrSteedCompositionBlue,
                record.IsSuspicious, record.EquipmentLockStateMask, CreateOptionalUtcTimestamp(record.EquipmentUnlockAtUtc, "equipment unlock"),
                record.EquipmentColor, record.CompositionProgress, record.InscribedSyndicateId, record.StackQuantity, CreateLifetime(record));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted item {record.ItemId} contains invalid hydrated state.", exception);
        }
    }

    private static ItemPlacement CreatePlacement(ItemRecord record)
    {
        switch (record.LocationKind)
        {
            case ItemLocationKind.Inventory:
                if (record.EquipmentSet is not null || record.EquipmentSlot is not null)
                {
                    throw new InvalidDataException($"Persisted inventory item {record.ItemId} contains equipment placement data.");
                }

                return ItemPlacement.CreateInventory();

            case ItemLocationKind.Equipment:
                if (record.EquipmentSet is not { } equipmentSet || record.EquipmentSlot is not { } equipmentSlot)
                {
                    throw new InvalidDataException($"Persisted equipment item {record.ItemId} is missing equipment placement data.");
                }

                try
                {
                    return ItemPlacement.CreateEquipment(EquipmentPosition.Create(equipmentSet, equipmentSlot));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Persisted equipment item {record.ItemId} contains an invalid equipment position.", exception);
                }

            default:
                throw new InvalidDataException($"Persisted item {record.ItemId} contains unsupported location kind {(byte)record.LocationKind}.");
        }
    }

    private static ItemLifetime CreateLifetime(ItemRecord record)
    {
        switch (record.LifetimeState)
        {
            case ItemLifetimeState.Permanent:
                if (record.LifetimeDurationSeconds is not null || record.LifetimeExpiresAtUtc is not null)
                {
                    throw new InvalidDataException($"Persisted permanent item {record.ItemId} contains lifetime payload.");
                }

                return ItemLifetime.CreatePermanent();

            case ItemLifetimeState.PendingActivation:
                if (record.LifetimeDurationSeconds is not > 0 || record.LifetimeExpiresAtUtc is not null)
                {
                    throw new InvalidDataException($"Persisted pending-lifetime item {record.ItemId} contains invalid lifetime payload.");
                }

                return ItemLifetime.CreatePendingActivation(record.LifetimeDurationSeconds.Value);

            case ItemLifetimeState.ActiveExpiry:
                if (record.LifetimeDurationSeconds is not null || record.LifetimeExpiresAtUtc is not { } expiresAtUtc)
                {
                    throw new InvalidDataException($"Persisted active-lifetime item {record.ItemId} contains invalid lifetime payload.");
                }

                return ItemLifetime.CreateActiveExpiry(CreateUtcTimestamp(expiresAtUtc, "lifetime expiration"));

            default:
                throw new InvalidDataException($"Persisted item {record.ItemId} contains unsupported lifetime state {(byte)record.LifetimeState}.");
        }
    }

    private static DateTimeOffset? CreateOptionalUtcTimestamp(DateTime? timestamp, string fieldName)
    {
        return timestamp is null ? null : CreateUtcTimestamp(timestamp.Value, fieldName);
    }

    private static DateTimeOffset CreateUtcTimestamp(DateTime timestamp, string fieldName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException($"Persisted item {fieldName} timestamp must be UTC.");
        }

        return new DateTimeOffset(timestamp);
    }
}
