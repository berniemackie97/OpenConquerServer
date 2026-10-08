using OpenConquer.Application.Items.Catalog;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Resolution;

public sealed class CharacterItemSetResolver(ICharacterItemSetRepository repository, ItemTypeCatalog itemTypes)
    : ICharacterItemSetResolver
{
    private readonly ICharacterItemSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ItemTypeCatalog _itemTypes = itemTypes ?? throw new ArgumentNullException(nameof(itemTypes));

    public async ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"An item-set resolution character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Item-set resolution requires a UTC timestamp.", nameof(utcNow));
        }

        cancellationToken.ThrowIfCancellationRequested();

        CharacterItemSet persistedItemSet = await _repository.LoadAsync(characterId, utcNow, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (persistedItemSet.CharacterId != characterId)
        {
            throw new InvalidDataException($"The persisted item set belongs to character {persistedItemSet.CharacterId}, not requested character {characterId}.");
        }

        int expiredItemCount = 0;

        foreach (CharacterItem item in persistedItemSet.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExpired(item.Lifetime, utcNow))
            {
                expiredItemCount++;
                continue;
            }

            if (!_itemTypes.TryGet(item.ItemTypeId, out ItemTypeDefinition? itemType))
            {
                throw new InvalidDataException($"Item {item.ItemId} references unknown item type {item.ItemTypeId}.");
            }

            CharacterItemTypeValidator.Validate(item, itemType);
        }

        if (expiredItemCount == 0)
        {
            return persistedItemSet;
        }

        CharacterItem[] activeItems = new CharacterItem[persistedItemSet.Count - expiredItemCount];
        int destinationIndex = 0;

        foreach (CharacterItem item in persistedItemSet.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsExpired(item.Lifetime, utcNow))
            {
                activeItems[destinationIndex++] = item;
            }
        }

        return new CharacterItemSet(characterId, activeItems);
    }

    private static bool IsExpired(ItemLifetime lifetime, DateTimeOffset utcNow)
    {
        return lifetime.State == ItemLifetimeState.ActiveExpiry && lifetime.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= utcNow;
    }
}
