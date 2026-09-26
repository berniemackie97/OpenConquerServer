using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using OpenConquer.Assets.IO;

namespace OpenConquer.Assets.Items;

public sealed class ItemTypeDatTable
{
    public const int DecodedTextSeed = 0x2537;
    public const int RetailCodePage = 936;

    private static readonly Lazy<Encoding> s_retailTextEncoding = new(CreateRetailTextEncoding);

    private readonly FrozenDictionary<uint, ItemTypeDatRecord> _recordsByItemTypeId;

    private ItemTypeDatTable(Dictionary<uint, ItemTypeDatRecord> recordsByItemTypeId)
    {
        _recordsByItemTypeId = recordsByItemTypeId.ToFrozenDictionary();
    }

    public int RecordCount => _recordsByItemTypeId.Count;

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

        Encoding encoding = s_retailTextEncoding.Value;
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
            uint life = ParseUInt32Field(fieldTexts, ItemTypeDatRecord.LifeFieldIndex, "life", sourceName, lineNumber);
            uint mana = ParseUInt32Field(fieldTexts, ItemTypeDatRecord.ManaFieldIndex, "mana", sourceName, lineNumber);

            recordsByItemTypeId[itemTypeId] = new ItemTypeDatRecord(itemTypeId, fieldTexts, requiredLevel, speedPercentOffset, life, mana);
            parsedRecordCount++;
        }

        return parsedRecordCount;
    }

    private static string[] NormalizeFieldTexts(string decodedLine, string sourceName, int lineNumber)
    {
        string[] fieldTexts = decodedLine.Split("@@", StringSplitOptions.None);

        if (fieldTexts.Length == ItemTypeDatRecord.NativeParsedFieldCount)
        {
            return fieldTexts;
        }

        if (fieldTexts.Length == ItemTypeDatRecord.NativeParsedFieldCount + 1 && fieldTexts[^1].Length == 0)
        {
            Array.Resize(ref fieldTexts, ItemTypeDatRecord.NativeParsedFieldCount);
            return fieldTexts;
        }

        throw new InvalidDataException($"{sourceName} record at line {lineNumber} has {fieldTexts.Length} split fields; expected {ItemTypeDatRecord.NativeParsedFieldCount} native fields with at most one terminal delimiter field.");
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

    private static Encoding CreateRetailTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            return Encoding.GetEncoding(RetailCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException exception)
        {
            throw new NotSupportedException($"Retail item assets require code page {RetailCodePage}, but that encoding is unavailable.", exception);
        }
    }
}
