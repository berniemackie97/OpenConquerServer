using OpenConquer.Domain.Items;

namespace OpenConquer.Domain.Tests.Items;

public sealed class ItemTypeExtendedDefinitionTests
{
    [Fact]
    public void ExtendedDefinition_PreservesNativeRequirementsEconomyAndAttackMetadata()
    {
        ItemTypeDefinition item = new(100001, "NativeItem", 15, -5, 100, 50,
            100, 200, 0, 1, requiredProfessionCode: 135,
            monopolyFlags: 4096, silverPrice: 250000,
            attackRange: 5, attackSpeed: 900, conquerPointsPrice: 999);

        Assert.Equal(135, item.RequiredProfessionCode);
        Assert.Equal(4096u, item.MonopolyFlags);
        Assert.Equal(250000u, item.SilverPrice);
        Assert.Equal(5, item.AttackRange);
        Assert.Equal(900, item.AttackSpeed);
        Assert.Equal(999u, item.ConquerPointsPrice);
    }

    [Fact]
    public void ExistingSyntheticDefinitions_DefaultAdditionalNativeValuesToZero()
    {
        ItemTypeDefinition item = new(100001, "Test", 1, 0, 0, 0, 1, 1, 0, 0);
        Assert.Equal(0, item.RequiredProfessionCode);
        Assert.Equal(0u, item.MonopolyFlags);
        Assert.Equal(0u, item.SilverPrice);
        Assert.Equal(0, item.AttackRange);
        Assert.Equal(0, item.AttackSpeed);
        Assert.Equal(0u, item.ConquerPointsPrice);
    }
}
