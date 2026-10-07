using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using OpenConquer.Assets.IO;

namespace OpenConquer.Assets.Items;

public sealed class ItemTypeDatTable
{
    public const int DecodedTextSeed = 0x2537;
    public const int TextCodePage = 936;

    private static readonly Lazy<Encoding> s_textEncoding = new(CreateTextEncoding);

    private readonly FrozenDictionary<uint, ItemTypeDatRecord> _recordsByItemTypeId;

    private ItemTypeDatTable(Dictionary<uint, ItemTypeDatRecord> recordsByItemTypeId)
    {
        _recordsByItemTypeId = recordsByItemTypeId.ToFrozenDictionary();
    }

    public int RecordCount => _recordsByItemTypeId.Count;
    public IEnumerable<ItemTypeDatRecord> Records => _recordsByItemTypeId.Values;

    public static ItemTypeDatTable Open(string primaryPath, string? supplementalPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryPath);

        byte[] primaryPayload = File.ReadAllBytes(primaryPath);
        byte[]? supplementalPayload = null;

        if (supplementalPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(supplementalPath);
            supplementalPayload = File.ReadAllBytes(supplementalPath);
        }

        return Parse(primaryPayload, supplementalPayload);
    }

    public static ItemTypeDatTable Parse(byte[] primaryPayload, byte[]? supplementalPayload = null)
    {
        ArgumentNullException.ThrowIfNull(primaryPayload);

        if (primaryPayload.Length == 0)
        {
            throw new InvalidDataException("Primary itemtype.dat payload is empty.");
        }

        if (supplementalPayload is { Length: 0 })
        {
            throw new InvalidDataException("Supplemental itemtype.dat payload is empty.");
        }

        Encoding encoding = s_textEncoding.Value;
        Dictionary<uint, ItemTypeDatRecord> recordsByItemTypeId = [];

        int primaryRecordCount = ParseDecodedLinesInto(recordsByItemTypeId,
            DecodedTextDatReader.DecodeLines(primaryPayload, DecodedTextSeed, encoding), "primary itemtype.dat");

        if (primaryRecordCount == 0)
        {
            throw new InvalidDataException("Primary itemtype.dat contains no item records.");
        }

        if (supplementalPayload is not null)
        {
            ParseDecodedLinesInto(recordsByItemTypeId,
                DecodedTextDatReader.DecodeLines(supplementalPayload, DecodedTextSeed, encoding), "supplemental itemtype.dat");
        }

        return new ItemTypeDatTable(recordsByItemTypeId);
    }

    public bool TryGetRecord(uint itemTypeId, out ItemTypeDatRecord record)
    {
        return _recordsByItemTypeId.TryGetValue(itemTypeId, out record!);
    }

    private static int ParseDecodedLinesInto(Dictionary<uint, ItemTypeDatRecord> recordsByItemTypeId, IReadOnlyList<string> decodedLines, string sourceName)
    {
        int parsedRecordCount = 0;

        for (int lineIndex = 0; lineIndex < decodedLines.Count; lineIndex++)
        {
            string decodedLine = decodedLines[lineIndex];
            int lineNumber = lineIndex + 1;

            if (string.IsNullOrWhiteSpace(decodedLine))
            {
                throw new InvalidDataException($"{sourceName} contains an empty record at line {lineNumber}.");
            }

            string[] fieldTexts = NormalizeFieldTexts(decodedLine, sourceName, lineNumber);

            uint itemTypeId = ParseUInt32Field(fieldTexts, ItemTypeDatRecord.ItemTypeIdFieldIndex, "item type ID", sourceName, lineNumber);

            if (itemTypeId == 0)
            {
                throw new InvalidDataException($"{sourceName} record at line {lineNumber} has zero item type ID.");
            }

            byte requiredLevel = ParseByteField(fieldTexts, ItemTypeDatRecord.RequiredLevelFieldIndex, "required level", sourceName, lineNumber);
            short speedPercentOffset = ParseInt16Field(fieldTexts, ItemTypeDatRecord.SpeedPercentOffsetFieldIndex, "speed percent offset", sourceName, lineNumber);
            short life = ParseInt16Field(fieldTexts, ItemTypeDatRecord.LifeFieldIndex, "life", sourceName, lineNumber);
            short mana = ParseInt16Field(fieldTexts, ItemTypeDatRecord.ManaFieldIndex, "mana", sourceName, lineNumber);
            ushort initialDurability = ParseUInt16Field(fieldTexts, ItemTypeDatRecord.InitialDurabilityFieldIndex, "initial durability", sourceName, lineNumber);
            ushort maximumDurability = ParseUInt16Field(fieldTexts, ItemTypeDatRecord.MaximumDurabilityFieldIndex, "maximum durability", sourceName, lineNumber);
            int staticLifetimeMinutes = ParseInt32Field(fieldTexts, ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex, "static lifetime minutes", sourceName, lineNumber);
            int stackCapacity = ParseInt32Field(fieldTexts, ItemTypeDatRecord.StackCapacityFieldIndex, "stack capacity", sourceName, lineNumber);

            recordsByItemTypeId[itemTypeId] = new ItemTypeDatRecord(itemTypeId, fieldTexts, requiredLevel, speedPercentOffset, life, mana, initialDurability, maximumDurability, staticLifetimeMinutes, stackCapacity);

            parsedRecordCount++;
        }

        return parsedRecordCount;
    }

    private static string[] NormalizeFieldTexts(string decodedLine, string sourceName, int lineNumber)
    {
        string[] fieldTexts = decodedLine.Split("@@");

        switch (fieldTexts.Length)
        {
            case ItemTypeDatRecord.RecordFieldCount:
                return fieldTexts;

            case ItemTypeDatRecord.RecordFieldCount + 1 when fieldTexts[^1].Length == 0:
                Array.Resize(ref fieldTexts, ItemTypeDatRecord.RecordFieldCount);
                return fieldTexts;

            default:
                throw new InvalidDataException(
                    $"{sourceName} record at line {lineNumber} has {fieldTexts.Length} split fields; expected {ItemTypeDatRecord.RecordFieldCount} fields with at most one terminal delimiter field.");
        }
    }

    private static uint ParseUInt32Field(string[] fieldTexts, int fieldIndex, string fieldName, string sourceName, int lineNumber)
    {
        string fieldText = fieldTexts[fieldIndex];

        if (!uint.TryParse(fieldText, NumberStyles.None, CultureInfo.InvariantCulture, out uint value))
        {
            throw new InvalidDataException($"{sourceName} record at line {lineNumber} has invalid {fieldName} '{fieldText}' at field {fieldIndex}.");
        }

        return value;
    }

    private static ushort ParseUInt16Field(string[] fieldTexts, int fieldIndex, string fieldName, string sourceName, int lineNumber)
    {
        string fieldText = fieldTexts[fieldIndex];

        if (!ushort.TryParse(fieldText, NumberStyles.None, CultureInfo.InvariantCulture, out ushort value))
        {
            throw new InvalidDataException($"{sourceName} record at line {lineNumber} has invalid {fieldName} '{fieldText}' at field {fieldIndex}.");
        }

        return value;
    }

    private static byte ParseByteField(string[] fieldTexts, int fieldIndex, string fieldName, string sourceName, int lineNumber)
    {
        string fieldText = fieldTexts[fieldIndex];

        if (!byte.TryParse(fieldText, NumberStyles.None, CultureInfo.InvariantCulture, out byte value))
        {
            throw new InvalidDataException($"{sourceName} record at line {lineNumber} has invalid {fieldName} '{fieldText}' at field {fieldIndex}.");
        }

        return value;
    }

    private static short ParseInt16Field(string[] fieldTexts, int fieldIndex, string fieldName, string sourceName, int lineNumber)
    {
        string fieldText = fieldTexts[fieldIndex];

        if (!short.TryParse(fieldText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out short value))
        {
            throw new InvalidDataException($"{sourceName} record at line {lineNumber} has invalid {fieldName} '{fieldText}' at field {fieldIndex}.");
        }

        return value;
    }

    private static int ParseInt32Field(string[] fieldTexts, int fieldIndex, string fieldName, string sourceName, int lineNumber)
    {
        string fieldText = fieldTexts[fieldIndex];

        if (!int.TryParse(fieldText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            throw new InvalidDataException($"{sourceName} record at line {lineNumber} has invalid {fieldName} '{fieldText}' at field {fieldIndex}.");
        }

        return value;
    }

    private static Encoding CreateTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            return Encoding.GetEncoding(TextCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException exception)
        {
            throw new NotSupportedException($"Item data requires code page {TextCodePage}, but that encoding is unavailable.", exception);
        }
    }
}
