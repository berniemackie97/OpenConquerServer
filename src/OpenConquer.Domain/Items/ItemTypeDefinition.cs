namespace OpenConquer.Domain.Items;

public sealed class ItemTypeDefinition
{
    private const uint SecondsPerMinute = 60;

    public const uint MaximumStaticLifetimeMinutes = (uint)int.MaxValue / SecondsPerMinute;

    public ItemTypeDefinition(uint itemTypeId, string name, byte requiredLevel, short speedPercentOffset, short life, short mana,
        ushort initialDurability, ushort maximumDurability, uint staticLifetimeMinutes, ushort stackCapacity,
        int requiredProfessionCode = 0, uint monopolyFlags = 0, uint silverPrice = 0,
        int attackRange = 0, int attackSpeed = 0, uint conquerPointsPrice = 0)
    {
        if (itemTypeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemTypeId), itemTypeId, "An item type requires a nonzero identity.");
        }

        ArgumentNullException.ThrowIfNull(name);

        if (staticLifetimeMinutes > MaximumStaticLifetimeMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(staticLifetimeMinutes), staticLifetimeMinutes, $"An item type static lifetime cannot exceed {MaximumStaticLifetimeMinutes} minutes.");
        }

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
        RequiredProfessionCode = requiredProfessionCode;
        MonopolyFlags = monopolyFlags;
        SilverPrice = silverPrice;
        AttackRange = attackRange;
        AttackSpeed = attackSpeed;
        ConquerPointsPrice = conquerPointsPrice;
    }

    public uint ItemTypeId { get; }
    public string Name { get; }
    public byte RequiredLevel { get; }
    public short SpeedPercentOffset { get; }
    public short Life { get; }
    public short Mana { get; }
    public ushort InitialDurability { get; }
    public ushort MaximumDurability { get; }
    public uint StaticLifetimeMinutes { get; }
    public ushort StackCapacity { get; }

    // Fields carried over from the 5517 itemtype.dat. They are kept as-is; no gameplay rule uses them yet.
    public int RequiredProfessionCode { get; }
    public uint MonopolyFlags { get; }
    public uint SilverPrice { get; }
    public int AttackRange { get; }
    public int AttackSpeed { get; }
    public uint ConquerPointsPrice { get; }

    public int StaticLifetimeDurationSeconds => checked((int)(StaticLifetimeMinutes * SecondsPerMinute));
    public ushort EffectiveStackCapacity => StackCapacity == 0 ? (ushort)1 : StackCapacity;
    public bool IsStackable => EffectiveStackCapacity > 1;
}
