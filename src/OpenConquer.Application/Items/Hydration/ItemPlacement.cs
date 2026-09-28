using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Hydration;

public readonly record struct ItemPlacement
{
    private const byte InventoryKind = 1;
    private const byte EquipmentKind = 2;

    private readonly byte _kind;

    private ItemPlacement(byte kind, EquipmentPosition? equipmentPosition)
    {
        _kind = kind;
        EquipmentPosition = equipmentPosition;
    }

    public EquipmentPosition? EquipmentPosition { get; }

    public bool IsInventory => _kind == InventoryKind && EquipmentPosition is null;
    public bool IsEquipment => _kind == EquipmentKind && EquipmentPosition is { IsValid: true };
    public bool IsValid => IsInventory || IsEquipment;

    public static ItemPlacement CreateInventory()
    {
        return new ItemPlacement(InventoryKind, equipmentPosition: null);
    }

    public static ItemPlacement CreateEquipment(EquipmentPosition equipmentPosition)
    {
        if (!equipmentPosition.IsValid)
        {
            throw new ArgumentException("An equipped item requires a valid equipment position.", nameof(equipmentPosition));
        }

        return new ItemPlacement(EquipmentKind, equipmentPosition);
    }
}
