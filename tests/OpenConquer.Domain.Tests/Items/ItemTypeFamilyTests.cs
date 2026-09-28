using OpenConquer.Domain.Items;

namespace OpenConquer.Domain.Tests.Items;

public sealed class ItemTypeFamilyTests
{
    [Theory]
    [InlineData(123_000u, 1)]
    [InlineData(1_123_500u, 1)]
    [InlineData(203_000u, 0x15)]
    [InlineData(601_000u, 4)]
    [InlineData(201_000u, 0x0C)]
    [InlineData(202_999u, 0x0C)]
    [InlineData(200_000u, 0x14)]
    [InlineData(350_000u, 0x10)]
    [InlineData(360_000u, 0x0F)]
    [InlineData(370_000u, 0x11)]
    [InlineData(380_000u, 0x12)]
    public void ResolveCategory_LeadingThousandsGroup_ReturnsNativeCategory(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveCategory(itemTypeId));
    }

    [Theory]
    [InlineData(300_000u, 0x0E)]
    [InlineData(300_010u, 0x0E)]
    [InlineData(300_001u, ItemTypeFamily.NotEquipment)]
    [InlineData(300_009u, ItemTypeFamily.NotEquipment)]
    public void ResolveCategory_Group300_RequiresMultipleOfTen(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveCategory(itemTypeId));
    }

    [Theory]
    [InlineData(700_000u, 9)]
    [InlineData(400_000u, 4)]
    [InlineData(600_000u, 4)]
    [InlineData(500_000u, 5)]
    [InlineData(900_000u, 6)]
    [InlineData(1_000_000u, 0)]
    [InlineData(1_900_000u, ItemTypeFamily.NotEquipment)]
    [InlineData(2_000_000u, 0x0A)]
    [InlineData(2_900_000u, 0x0A)]
    [InlineData(3_000_000u, ItemTypeFamily.NotEquipment)]
    public void ResolveCategory_HundredThousandsGroup_ReturnsNativeCategory(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveCategory(itemTypeId));
    }

    [Theory]
    [InlineData(110_000u, 1)]
    [InlineData(140_000u, 1)]
    [InlineData(170_000u, 1)]
    [InlineData(120_000u, 2)]
    [InlineData(130_000u, 3)]
    [InlineData(150_000u, 7)]
    [InlineData(160_000u, 8)]
    [InlineData(180_000u, 0x0B)]
    [InlineData(190_000u, 0x0B)]
    [InlineData(100_000u, ItemTypeFamily.NotEquipment)]
    public void ResolveCategory_OneHundredThousandsGroup_UsesNativeSecondaryDivision(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveCategory(itemTypeId));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(800_000u)]
    [InlineData(1_100_000u)]
    [InlineData(3_000_000u)]
    [InlineData(uint.MaxValue)]
    public void ResolveCategory_UnclassifiedType_ReturnsNotEquipment(uint itemTypeId)
    {
        Assert.Equal(ItemTypeFamily.NotEquipment, ItemTypeFamily.ResolveCategory(itemTypeId));
    }

    [Theory]
    [InlineData(201_000u, 1)]
    [InlineData(201_999u, 1)]
    [InlineData(202_000u, 2)]
    [InlineData(202_999u, 2)]
    public void ResolveSubKind_TalismanFamilies_UseThousandsDigit(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveSubKind(itemTypeId));
    }

    [Theory]
    [InlineData(400_000u, 0)]
    [InlineData(400_999u, 0)]
    [InlineData(401_000u, 1_000)]
    [InlineData(421_309u, 21_000)]
    [InlineData(499_999u, 99_000)]
    [InlineData(500_129u, 0)]
    [InlineData(510_129u, 10_000)]
    [InlineData(599_999u, 99_000)]
    public void ResolveSubKind_CategoryFourAndFiveFamilies_UseThousandsScale(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveSubKind(itemTypeId));
    }

    [Theory]
    [InlineData(700_000u, 0)]
    [InlineData(709_999u, 0)]
    [InlineData(710_000u, 10_000)]
    [InlineData(730_000u, 30_000)]
    [InlineData(799_999u, 90_000)]
    [InlineData(1_000_000u, 0)]
    [InlineData(1_040_000u, 40_000)]
    [InlineData(1_099_999u, 90_000)]
    public void ResolveSubKind_CategorySevenAndTenFamilies_UseTenThousandsScale(uint itemTypeId, int expected)
    {
        Assert.Equal(expected, ItemTypeFamily.ResolveSubKind(itemTypeId));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(123_000u)]
    [InlineData(300_000u)]
    [InlineData(600_000u)]
    [InlineData(900_000u)]
    [InlineData(2_000_000u)]
    [InlineData(uint.MaxValue)]
    public void ResolveSubKind_UnsupportedFamily_ReturnsNotEquipment(uint itemTypeId)
    {
        Assert.Equal(ItemTypeFamily.NotEquipment, ItemTypeFamily.ResolveSubKind(itemTypeId));
    }

    [Theory]
    [InlineData(500_000u, true)]
    [InlineData(500_999u, true)]
    [InlineData(501_000u, false)]
    [InlineData(510_129u, false)]
    [InlineData(400_000u, false)]
    [InlineData(1_000_000u, false)]
    public void IsBow_ReturnsNativeBowClassification(uint itemTypeId, bool expected)
    {
        Assert.Equal(expected, ItemTypeFamily.IsBow(itemTypeId));
    }

    [Theory]
    [InlineData(1_050_000u, true)]
    [InlineData(1_059_999u, true)]
    [InlineData(1_000_000u, false)]
    [InlineData(1_049_999u, false)]
    [InlineData(1_060_000u, false)]
    [InlineData(750_000u, false)]
    [InlineData(500_000u, false)]
    public void IsArrow_ReturnsNativeArrowClassification(uint itemTypeId, bool expected)
    {
        Assert.Equal(expected, ItemTypeFamily.IsArrow(itemTypeId));
    }
}
