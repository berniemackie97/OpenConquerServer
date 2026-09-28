using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Hydration;

public sealed class ItemPlacementTests
{
    [Fact]
    public void CreateInventory_ReturnsValidInventoryPlacement()
    {
        ItemPlacement placement = ItemPlacement.CreateInventory();

        Assert.True(placement.IsInventory);
        Assert.False(placement.IsEquipment);
        Assert.True(placement.IsValid);
        Assert.Null(placement.EquipmentPosition);
    }

    [Theory]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Headwear)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.RidingCrop)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Headwear)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Garment)]
    public void CreateEquipment_ValidPosition_ReturnsValidEquipmentPlacement(EquipmentSet set, EquipmentSlot slot)
    {
        EquipmentPosition position = EquipmentPosition.Create(set, slot);

        ItemPlacement placement = ItemPlacement.CreateEquipment(position);

        Assert.False(placement.IsInventory);
        Assert.True(placement.IsEquipment);
        Assert.True(placement.IsValid);
        Assert.Equal(position, placement.EquipmentPosition);
    }

    [Fact]
    public void CreateEquipment_InvalidPosition_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ItemPlacement.CreateEquipment(default));

        Assert.Equal("equipmentPosition", exception.ParamName);
    }

    [Fact]
    public void Default_IsInvalid()
    {
        ItemPlacement placement = default;

        Assert.False(placement.IsInventory);
        Assert.False(placement.IsEquipment);
        Assert.False(placement.IsValid);
        Assert.Null(placement.EquipmentPosition);
    }
}
