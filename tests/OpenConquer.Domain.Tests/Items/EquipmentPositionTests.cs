using OpenConquer.Domain.Items;

namespace OpenConquer.Domain.Tests.Items;

public sealed class EquipmentPositionTests
{
    [Theory]
    [InlineData(EquipmentSlot.Headwear)]
    [InlineData(EquipmentSlot.Necklace)]
    [InlineData(EquipmentSlot.Armor)]
    [InlineData(EquipmentSlot.RightHand)]
    [InlineData(EquipmentSlot.LeftHand)]
    [InlineData(EquipmentSlot.Ring)]
    [InlineData(EquipmentSlot.Bottle)]
    [InlineData(EquipmentSlot.Boots)]
    [InlineData(EquipmentSlot.Garment)]
    [InlineData(EquipmentSlot.Fan)]
    [InlineData(EquipmentSlot.Tower)]
    [InlineData(EquipmentSlot.Steed)]
    [InlineData(EquipmentSlot.RightWeaponAccessory)]
    [InlineData(EquipmentSlot.LeftWeaponAccessory)]
    [InlineData(EquipmentSlot.SteedArmor)]
    [InlineData(EquipmentSlot.RidingCrop)]
    public void Create_MainSetSlot_ReturnsValidPosition(EquipmentSlot slot)
    {
        EquipmentPosition position = EquipmentPosition.Create(EquipmentSet.Main, slot);

        Assert.Equal(EquipmentSet.Main, position.Set);
        Assert.Equal(slot, position.Slot);
        Assert.True(position.IsValid);
    }

    [Theory]
    [InlineData(EquipmentSlot.Headwear)]
    [InlineData(EquipmentSlot.Necklace)]
    [InlineData(EquipmentSlot.Armor)]
    [InlineData(EquipmentSlot.RightHand)]
    [InlineData(EquipmentSlot.LeftHand)]
    [InlineData(EquipmentSlot.Ring)]
    [InlineData(EquipmentSlot.Bottle)]
    [InlineData(EquipmentSlot.Boots)]
    [InlineData(EquipmentSlot.Garment)]
    public void Create_AlternateSetSupportedSlot_ReturnsValidPosition(EquipmentSlot slot)
    {
        EquipmentPosition position = EquipmentPosition.Create(EquipmentSet.Alternate, slot);

        Assert.Equal(EquipmentSet.Alternate, position.Set);
        Assert.Equal(slot, position.Slot);
        Assert.True(position.IsValid);
    }

    [Theory]
    [InlineData(EquipmentSlot.Fan)]
    [InlineData(EquipmentSlot.Tower)]
    [InlineData(EquipmentSlot.Steed)]
    [InlineData(EquipmentSlot.RightWeaponAccessory)]
    [InlineData(EquipmentSlot.LeftWeaponAccessory)]
    [InlineData(EquipmentSlot.SteedArmor)]
    [InlineData(EquipmentSlot.RidingCrop)]
    public void Create_AlternateSetUnsupportedSlot_ThrowsArgumentException(EquipmentSlot slot)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => EquipmentPosition.Create(EquipmentSet.Alternate, slot));

        Assert.Equal("slot", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(byte.MaxValue)]
    public void Create_UndefinedSet_ThrowsArgumentOutOfRangeException(byte value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EquipmentPosition.Create((EquipmentSet)value, EquipmentSlot.Headwear));

        Assert.Equal("set", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(byte.MaxValue)]
    public void Create_UndefinedSlot_ThrowsArgumentOutOfRangeException(byte value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EquipmentPosition.Create(EquipmentSet.Main, (EquipmentSlot)value));

        Assert.Equal("slot", exception.ParamName);
    }

    [Theory]
    [InlineData(EquipmentSlot.Headwear)]
    [InlineData(EquipmentSlot.Necklace)]
    [InlineData(EquipmentSlot.Armor)]
    [InlineData(EquipmentSlot.RightHand)]
    [InlineData(EquipmentSlot.LeftHand)]
    [InlineData(EquipmentSlot.Ring)]
    [InlineData(EquipmentSlot.Bottle)]
    [InlineData(EquipmentSlot.Boots)]
    [InlineData(EquipmentSlot.Garment)]
    public void SupportsAlternateSet_SupportedSlot_ReturnsTrue(EquipmentSlot slot)
    {
        Assert.True(EquipmentPosition.SupportsAlternateSet(slot));
    }

    [Theory]
    [InlineData(EquipmentSlot.Fan)]
    [InlineData(EquipmentSlot.Tower)]
    [InlineData(EquipmentSlot.Steed)]
    [InlineData(EquipmentSlot.RightWeaponAccessory)]
    [InlineData(EquipmentSlot.LeftWeaponAccessory)]
    [InlineData(EquipmentSlot.SteedArmor)]
    [InlineData(EquipmentSlot.RidingCrop)]
    [InlineData((EquipmentSlot)0)]
    [InlineData((EquipmentSlot)17)]
    public void SupportsAlternateSet_UnsupportedSlot_ReturnsFalse(EquipmentSlot slot)
    {
        Assert.False(EquipmentPosition.SupportsAlternateSet(slot));
    }

    [Fact]
    public void Default_IsInvalid()
    {
        EquipmentPosition position = default;

        Assert.False(position.IsValid);
    }
}
