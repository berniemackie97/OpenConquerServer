using OpenConquer.Domain.Items;

namespace OpenConquer.Domain.Tests.Items;

public sealed class ItemTypeDefinitionTests
{
    [Fact]
    public void Constructor_ValidDefinition_PreservesStaticState()
    {
        ItemTypeDefinition definition = new(
            itemTypeId: 100001,
            name: "TestItem",
            requiredLevel: byte.MaxValue,
            speedPercentOffset: short.MinValue,
            life: short.MaxValue,
            mana: short.MinValue,
            initialDurability: ushort.MaxValue,
            maximumDurability: ushort.MaxValue,
            staticLifetimeMinutes: uint.MaxValue,
            stackCapacity: ushort.MaxValue);

        Assert.Equal(100001u, definition.ItemTypeId);
        Assert.Equal("TestItem", definition.Name);
        Assert.Equal(byte.MaxValue, definition.RequiredLevel);
        Assert.Equal(short.MinValue, definition.SpeedPercentOffset);
        Assert.Equal(short.MaxValue, definition.Life);
        Assert.Equal(short.MinValue, definition.Mana);
        Assert.Equal(ushort.MaxValue, definition.InitialDurability);
        Assert.Equal(ushort.MaxValue, definition.MaximumDurability);
        Assert.Equal(uint.MaxValue, definition.StaticLifetimeMinutes);
        Assert.Equal(ushort.MaxValue, definition.StackCapacity);
        Assert.Equal(ushort.MaxValue, definition.EffectiveStackCapacity);
        Assert.True(definition.IsStackable);
    }

    [Fact]
    public void Constructor_ZeroStaticValues_ArePreserved()
    {
        ItemTypeDefinition definition = Create(stackCapacity: 0);

        Assert.Equal((byte)0, definition.RequiredLevel);
        Assert.Equal((short)0, definition.SpeedPercentOffset);
        Assert.Equal((short)0, definition.Life);
        Assert.Equal((short)0, definition.Mana);
        Assert.Equal((ushort)0, definition.InitialDurability);
        Assert.Equal((ushort)0, definition.MaximumDurability);
        Assert.Equal(0u, definition.StaticLifetimeMinutes);
        Assert.Equal((ushort)0, definition.StackCapacity);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    public void EffectiveStackCapacity_ZeroOrOne_IsOneAndNotStackable(ushort stackCapacity)
    {
        ItemTypeDefinition definition = Create(stackCapacity);

        Assert.Equal((ushort)1, definition.EffectiveStackCapacity);
        Assert.False(definition.IsStackable);
    }

    [Theory]
    [InlineData((ushort)3)]
    [InlineData((ushort)5)]
    [InlineData((ushort)10)]
    [InlineData((ushort)100)]
    [InlineData(ushort.MaxValue)]
    public void EffectiveStackCapacity_AboveOne_PreservesCapacityAndIsStackable(ushort stackCapacity)
    {
        ItemTypeDefinition definition = Create(stackCapacity);

        Assert.Equal(stackCapacity, definition.EffectiveStackCapacity);
        Assert.True(definition.IsStackable);
    }

    [Fact]
    public void Constructor_ZeroItemTypeId_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ItemTypeDefinition(
                itemTypeId: 0,
                name: "TestItem",
                requiredLevel: 0,
                speedPercentOffset: 0,
                life: 0,
                mana: 0,
                initialDurability: 0,
                maximumDurability: 0,
                staticLifetimeMinutes: 0,
                stackCapacity: 0));

        Assert.Equal("itemTypeId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullName_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new ItemTypeDefinition(
                itemTypeId: 1,
                name: null!,
                requiredLevel: 0,
                speedPercentOffset: 0,
                life: 0,
                mana: 0,
                initialDurability: 0,
                maximumDurability: 0,
                staticLifetimeMinutes: 0,
                stackCapacity: 0));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_EmptyName_IsPreserved()
    {
        ItemTypeDefinition definition = new(
            itemTypeId: 1,
            name: string.Empty,
            requiredLevel: 0,
            speedPercentOffset: 0,
            life: 0,
            mana: 0,
            initialDurability: 0,
            maximumDurability: 0,
            staticLifetimeMinutes: 0,
            stackCapacity: 0);

        Assert.Equal(string.Empty, definition.Name);
    }

    private static ItemTypeDefinition Create(ushort stackCapacity)
    {
        return new ItemTypeDefinition(
            itemTypeId: 100001,
            name: "TestItem",
            requiredLevel: 0,
            speedPercentOffset: 0,
            life: 0,
            mana: 0,
            initialDurability: 0,
            maximumDurability: 0,
            staticLifetimeMinutes: 0,
            stackCapacity);
    }
}
