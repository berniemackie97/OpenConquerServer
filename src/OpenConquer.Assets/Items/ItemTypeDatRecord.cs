using System.Collections.ObjectModel;

namespace OpenConquer.Assets.Items;

public sealed class ItemTypeDatRecord
{
    public const int NativeParsedFieldCount = 59;

    public const int ItemTypeIdFieldIndex = 0;
    public const int NameFieldIndex = 1;
    public const int RequiredLevelFieldIndex = 4;
    public const int SpeedPercentOffsetFieldIndex = 18;
    public const int LifeFieldIndex = 19;
    public const int ManaFieldIndex = 20;
    public const int StaticLifetimeMinutesFieldIndex = 39;
    public const int StackCapacityFieldIndex = 47;
    public const int TypeDescriptionFieldIndex = 53;
    public const int ItemDescriptionFieldIndex = 54;

    private readonly ReadOnlyCollection<string> _fieldTexts;

    internal ItemTypeDatRecord(uint itemTypeId, string[] fieldTexts, byte requiredLevel, short speedPercentOffset,
        short life, short mana, int staticLifetimeMinutes, int stackCapacity)
    {
        ArgumentNullException.ThrowIfNull(fieldTexts);

        if (itemTypeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemTypeId), "An item-type record must have a nonzero item type ID.");
        }

        if (fieldTexts.Length != NativeParsedFieldCount)
        {
            throw new ArgumentException($"An item-type record must contain exactly {NativeParsedFieldCount} native fields.", nameof(fieldTexts));
        }

        ItemTypeId = itemTypeId;
        RequiredLevel = requiredLevel;
        SpeedPercentOffset = speedPercentOffset;
        Life = life;
        Mana = mana;
        StaticLifetimeMinutes = staticLifetimeMinutes;
        StackCapacity = stackCapacity;
        _fieldTexts = Array.AsReadOnly((string[])fieldTexts.Clone());
    }

    public uint ItemTypeId { get; }
    public string Name => _fieldTexts[NameFieldIndex];
    public byte RequiredLevel { get; }
    public short SpeedPercentOffset { get; }
    public short Life { get; }
    public short Mana { get; }
    public int StaticLifetimeMinutes { get; }
    public int StackCapacity { get; }
    public string TypeDescription => _fieldTexts[TypeDescriptionFieldIndex];
    public string ItemDescription => _fieldTexts[ItemDescriptionFieldIndex];

    public IReadOnlyList<string> FieldTexts => _fieldTexts;
    public int FieldCount => _fieldTexts.Count;
}
