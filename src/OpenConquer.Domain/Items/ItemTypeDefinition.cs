namespace OpenConquer.Domain.Items;

public sealed class ItemTypeDefinition
{
    public ItemTypeDefinition(uint itemTypeId, string name, byte requiredLevel, short speedPercentOffset, short life, short mana,
        ushort initialDurability, ushort maximumDurability, uint staticLifetimeMinutes, ushort stackCapacity)
    {
        if (itemTypeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemTypeId), itemTypeId, "An item type requires a nonzero identity.");
        }

        ArgumentNullException.ThrowIfNull(name);

        ItemTypeId = itemTypeId;
        Name = name;
        RequiredLevel = requiredLevel;
        SpeedPercentOffset = speedPercentOffset;
        Life = life;
        Mana = mana;
        InitialDurability = initialDurability;
        MaximumDurability = maximumDurability;
        StaticLifetimeMinutes = staticLifetimeMinutes;
        StackCapacity = stackCapacity;
    }

    public uint ItemTypeId
    {
        get;
    }

    public string Name
    {
        get;
    }

    public byte RequiredLevel
    {
        get;
    }

    public short SpeedPercentOffset
    {
        get;
    }

    public short Life
    {
        get;
    }

    public short Mana
    {
        get;
    }

    public ushort InitialDurability
    {
        get;
    }

    public ushort MaximumDurability
    {
        get;
    }

    public uint StaticLifetimeMinutes
    {
        get;
    }

    public ushort StackCapacity
    {
        get;
    }

    public ushort EffectiveStackCapacity => StackCapacity == 0 ? (ushort)1 : StackCapacity;
    public bool IsStackable => EffectiveStackCapacity > 1;
}
