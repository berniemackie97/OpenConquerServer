using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Resolution;

public static class CharacterItemTypeValidator
{
    public static void Validate(CharacterItem item, ItemTypeDefinition itemType)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(itemType);

        if (item.ItemTypeId != itemType.ItemTypeId)
        {
            throw new ArgumentException($"Item {item.ItemId} references item type {item.ItemTypeId}, not supplied definition {itemType.ItemTypeId}.", nameof(itemType));
        }

        if (item.StackQuantity > itemType.EffectiveStackCapacity)
        {
            throw new InvalidDataException($"Item {item.ItemId} stack quantity {item.StackQuantity} exceeds item type {item.ItemTypeId} capacity {itemType.EffectiveStackCapacity}.");
        }

        switch (item.Lifetime.State)
        {
            case ItemLifetimeState.Permanent:
                if (itemType.StaticLifetimeMinutes > 0)
                {
                    throw new InvalidDataException($"Permanent item {item.ItemId} uses item type {item.ItemTypeId} with positive static lifetime {itemType.StaticLifetimeMinutes} minutes.");
                }

                return;

            case ItemLifetimeState.PendingActivation:
                if (itemType.StaticLifetimeMinutes == 0)
                {
                    throw new InvalidDataException($"Pending-lifetime item {item.ItemId} uses item type {item.ItemTypeId} without a positive static lifetime.");
                }

                int expectedDurationSeconds;

                try
                {
                    expectedDurationSeconds = checked((int)(itemType.StaticLifetimeMinutes * 60L));
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException($"Item type {item.ItemTypeId} static lifetime cannot be represented in seconds.", exception);
                }

                if (item.Lifetime.PendingActivationDurationSeconds != expectedDurationSeconds)
                {
                    throw new InvalidDataException($"Pending-lifetime item {item.ItemId} duration does not match item type {item.ItemTypeId} static lifetime.");
                }

                return;

            case ItemLifetimeState.ActiveExpiry:
                return;

            default:
                throw new InvalidDataException($"Item {item.ItemId} contains unsupported lifetime state {(byte)item.Lifetime.State}.");
        }
    }
}
