using System.Globalization;
using System.Text;
using OpenConquer.Assets.Items;

namespace OpenConquer.Assets.Tests.Items;

public sealed class ItemTypeDatTableTests
{
    private const int SeedTableLength = 128;

    private static readonly Encoding s_retailEncoding = CreateRetailEncoding();

    [Fact]
    public void Parse_PrimaryRetailRecord_NormalizesFieldsAndDecodesCp936()
    {
        string[] fields = CreateFields(100001, "青虹剑", 15, 5, 120, 30, "Weapon", "测试物品",
            staticLifetimeMinutes: 10080, stackCapacity: 20);
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        Assert.Equal(1, table.RecordCount);
        Assert.True(table.TryGetRecord(100001, out ItemTypeDatRecord record));
        Assert.Equal(100001u, record.ItemTypeId);
        Assert.Equal("青虹剑", record.Name);
        Assert.Equal((byte)15, record.RequiredLevel);
        Assert.Equal((short)5, record.SpeedPercentOffset);
        Assert.Equal((short)120, record.Life);
        Assert.Equal((short)30, record.Mana);
        Assert.Equal(10080, record.StaticLifetimeMinutes);
        Assert.Equal(20, record.StackCapacity);
        Assert.Equal("Weapon", record.TypeDescription);
        Assert.Equal("测试物品", record.ItemDescription);
        Assert.Equal(ItemTypeDatRecord.NativeParsedFieldCount, record.FieldCount);
        Assert.Equal("10080", record.FieldTexts[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex]);
        Assert.Equal("20", record.FieldTexts[ItemTypeDatRecord.StackCapacityFieldIndex]);
        Assert.Equal("Weapon", record.FieldTexts[ItemTypeDatRecord.TypeDescriptionFieldIndex]);
        Assert.Equal("测试物品", record.FieldTexts[ItemTypeDatRecord.ItemDescriptionFieldIndex]);
    }

    [Fact]
    public void Parse_SignedInt16Fields_PreserveSignedRepresentation()
    {
        string[] fields = CreateFields(100001, "TestItem", speedPercentOffset: -15, life: short.MinValue, mana: short.MaxValue);
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        Assert.True(table.TryGetRecord(100001, out ItemTypeDatRecord record));
        Assert.Equal((short)-15, record.SpeedPercentOffset);
        Assert.Equal(short.MinValue, record.Life);
        Assert.Equal(short.MaxValue, record.Mana);
    }

    [Fact]
    public void Parse_SignedInt32Fields_PreserveSignedRepresentation()
    {
        string[] fields = CreateFields(100001, "TestItem", staticLifetimeMinutes: int.MinValue, stackCapacity: int.MaxValue);
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        Assert.True(table.TryGetRecord(100001, out ItemTypeDatRecord record));
        Assert.Equal(int.MinValue, record.StaticLifetimeMinutes);
        Assert.Equal(int.MaxValue, record.StackCapacity);
    }

    [Fact]
    public void Parse_RetailTextTokens_RemainRawInAssetLayer()
    {
        string[] fields = CreateFields(100001, "Dragon~Ball", typeDescription: "Warrior`sHelmet", itemDescription: "Fixed.~It~cannot~be~upgraded.");
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        Assert.True(table.TryGetRecord(100001, out ItemTypeDatRecord record));
        Assert.Equal("Dragon~Ball", record.Name);
        Assert.Equal("Warrior`sHelmet", record.TypeDescription);
        Assert.Equal("Fixed.~It~cannot~be~upgraded.", record.ItemDescription);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_RecordWithOrWithoutTerminalDelimiter_NormalizesToNativeFieldCount(bool terminalDelimiter)
    {
        string[] fields = CreateFields(100001, "TestItem");
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields, terminalDelimiter)));

        Assert.True(table.TryGetRecord(100001, out ItemTypeDatRecord record));
        Assert.Equal(ItemTypeDatRecord.NativeParsedFieldCount, record.FieldCount);
    }

    [Fact]
    public void Parse_SupplementalRecords_OverridePrimaryAndAddNewRecord()
    {
        string primary = CreateLine(CreateFields(100000, "OriginalBlade", typeDescription: "Weapon", itemDescription: "Primary",
            staticLifetimeMinutes: 0, stackCapacity: 1));
        string supplemental = string.Join("\r\n",
            CreateLine(CreateFields(100000, "OverrideBlade", typeDescription: "Weapon2", itemDescription: "Supplemental",
                staticLifetimeMinutes: 43200, stackCapacity: 10)),
            CreateLine(CreateFields(100002, "SecondItem", typeDescription: "Other", itemDescription: "Second",
                staticLifetimeMinutes: 10080, stackCapacity: 20)));

        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(primary), EncodeText(supplemental));

        Assert.Equal(2, table.RecordCount);
        Assert.True(table.TryGetRecord(100000, out ItemTypeDatRecord overridden));
        Assert.Equal("OverrideBlade", overridden.Name);
        Assert.Equal(43200, overridden.StaticLifetimeMinutes);
        Assert.Equal(10, overridden.StackCapacity);
        Assert.Equal("Weapon2", overridden.TypeDescription);
        Assert.Equal("Supplemental", overridden.ItemDescription);

        Assert.True(table.TryGetRecord(100002, out ItemTypeDatRecord added));
        Assert.Equal("SecondItem", added.Name);
        Assert.Equal(10080, added.StaticLifetimeMinutes);
        Assert.Equal(20, added.StackCapacity);
        Assert.Equal("Other", added.TypeDescription);
        Assert.Equal("Second", added.ItemDescription);
    }

    [Fact]
    public void Parse_DuplicatePrimaryItemTypeId_LastRecordWins()
    {
        string decodedText = string.Join("\r\n",
            CreateLine(CreateFields(100, "First", staticLifetimeMinutes: 10, stackCapacity: 2)),
            CreateLine(CreateFields(100, "Second", staticLifetimeMinutes: 20, stackCapacity: 3)));

        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(decodedText));

        Assert.Equal(1, table.RecordCount);
        Assert.True(table.TryGetRecord(100, out ItemTypeDatRecord record));
        Assert.Equal("Second", record.Name);
        Assert.Equal(20, record.StaticLifetimeMinutes);
        Assert.Equal(3, record.StackCapacity);
    }

    [Fact]
    public void Parse_EmptyPrimaryPayload_ThrowsInvalidDataException()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse([]));

        Assert.Contains("Primary itemtype.dat payload is empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptySupplementalPayload_ThrowsInvalidDataException()
    {
        byte[] primaryPayload = EncodeText(CreateLine(CreateFields(100001, "TestItem")));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(primaryPayload, []));

        Assert.Contains("Supplemental itemtype.dat payload is empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_BlankDecodedRecord_ThrowsWithLineNumber()
    {
        string decodedText = $"{CreateLine(CreateFields(100001, "First"))}\r\n\r\n{CreateLine(CreateFields(100002, "Second"))}";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(decodedText)));

        Assert.Contains("primary itemtype.dat contains an empty record at line 2.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidItemTypeId_ThrowsWithSourceAndLineNumber()
    {
        string[] fields = CreateFields(100001, "TestItem");
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = "abc";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(CreateLine(fields))));

        Assert.Contains("primary itemtype.dat record at line 1 has invalid item type ID 'abc' at field 0.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ZeroItemTypeId_ThrowsWithSourceAndLineNumber()
    {
        string[] fields = CreateFields(100001, "TestItem");
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = "0";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(CreateLine(fields))));

        Assert.Contains("primary itemtype.dat record at line 1 has zero item type ID.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ItemTypeDatRecord.RequiredLevelFieldIndex, "256", "required level")]
    [InlineData(ItemTypeDatRecord.SpeedPercentOffsetFieldIndex, "32768", "speed percent offset")]
    [InlineData(ItemTypeDatRecord.SpeedPercentOffsetFieldIndex, "-32769", "speed percent offset")]
    [InlineData(ItemTypeDatRecord.LifeFieldIndex, "32768", "life")]
    [InlineData(ItemTypeDatRecord.LifeFieldIndex, "-32769", "life")]
    [InlineData(ItemTypeDatRecord.ManaFieldIndex, "32768", "mana")]
    [InlineData(ItemTypeDatRecord.ManaFieldIndex, "-32769", "mana")]
    [InlineData(ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex, "2147483648", "static lifetime minutes")]
    [InlineData(ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex, "-2147483649", "static lifetime minutes")]
    [InlineData(ItemTypeDatRecord.StackCapacityFieldIndex, "2147483648", "stack capacity")]
    [InlineData(ItemTypeDatRecord.StackCapacityFieldIndex, "-2147483649", "stack capacity")]
    public void Parse_InvalidVerifiedNumericField_ThrowsWithFieldDiagnostics(int fieldIndex, string fieldValue, string fieldName)
    {
        string[] fields = CreateFields(100001, "TestItem");
        fields[fieldIndex] = fieldValue;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(CreateLine(fields))));

        Assert.Contains($"invalid {fieldName} '{fieldValue}' at field {fieldIndex}.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TruncatedRecord_ThrowsInvalidDataException()
    {
        string[] fields = CreateFields(100001, "TestItem")[..(ItemTypeDatRecord.NativeParsedFieldCount - 1)];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(CreateLine(fields, false))));

        Assert.Contains("has 58 split fields; expected 59 native fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AdditionalRealField_ThrowsInvalidDataException()
    {
        string[] fields = [.. CreateFields(100001, "TestItem"), "unexpected"];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(CreateLine(fields, false))));

        Assert.Contains("has 60 split fields; expected 59 native fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MultipleTerminalDelimiterFields_ThrowsInvalidDataException()
    {
        string decodedText = CreateLine(CreateFields(100001, "TestItem")) + "@@";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ItemTypeDatTable.Parse(EncodeText(decodedText)));

        Assert.Contains("has 61 split fields; expected 59 native fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidCp936_ThrowsDecoderFallbackException()
    {
        byte[] decodedPayload = [0x81];
        byte[] primaryPayload = EncodeDecodedBytes(decodedPayload);

        Assert.Throws<DecoderFallbackException>(() => ItemTypeDatTable.Parse(primaryPayload));
    }

    [Fact]
    public void TryGetRecord_UnknownItemTypeId_ReturnsFalse()
    {
        ItemTypeDatTable table = ItemTypeDatTable.Parse(EncodeText(CreateLine(CreateFields(100001, "TestItem"))));

        Assert.False(table.TryGetRecord(999999, out _));
    }

    private static string[] CreateFields(uint itemTypeId, string name, byte requiredLevel = 0, short speedPercentOffset = 0, short life = 0,
        short mana = 0, string typeDescription = "", string itemDescription = "", int staticLifetimeMinutes = 0, int stackCapacity = 0)
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.NativeParsedFieldCount).ToArray();
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = itemTypeId.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.NameFieldIndex] = name;
        fields[ItemTypeDatRecord.RequiredLevelFieldIndex] = requiredLevel.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.SpeedPercentOffsetFieldIndex] = speedPercentOffset.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.LifeFieldIndex] = life.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.ManaFieldIndex] = mana.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex] = staticLifetimeMinutes.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.StackCapacityFieldIndex] = stackCapacity.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.TypeDescriptionFieldIndex] = typeDescription;
        fields[ItemTypeDatRecord.ItemDescriptionFieldIndex] = itemDescription;
        return fields;
    }

    private static string CreateLine(string[] fields, bool terminalDelimiter = true)
    {
        return string.Join("@@", fields) + (terminalDelimiter ? "@@" : string.Empty);
    }

    private static byte[] EncodeText(string decodedText)
    {
        return EncodeDecodedBytes(s_retailEncoding.GetBytes(decodedText));
    }

    private static byte[] EncodeDecodedBytes(ReadOnlySpan<byte> decodedPayload)
    {
        byte[] encodedPayload = decodedPayload.ToArray();
        Span<byte> seedTable = stackalloc byte[SeedTableLength];
        BuildSeedTable(seedTable, ItemTypeDatTable.DecodedTextSeed);

        for (int index = 0; index < encodedPayload.Length; index++)
        {
            int rotation = index & 7;
            byte transformed = rotation == 0 ? encodedPayload[index] : RotateLeft(encodedPayload[index], rotation);
            encodedPayload[index] = (byte)(transformed ^ seedTable[index % SeedTableLength]);
        }

        return encodedPayload;
    }

    private static void BuildSeedTable(Span<byte> seedTable, int seed)
    {
        uint state = unchecked((uint)seed);
        for (int index = 0; index < seedTable.Length; index++)
        {
            state = unchecked(state * 214013u + 2531011u);
            seedTable[index] = (byte)(((state >> 16) & 0x7FFFu) % 256u);
        }
    }

    private static byte RotateLeft(byte value, int bitCount)
    {
        return (byte)((value << bitCount) | (value >> (8 - bitCount)));
    }

    private static Encoding CreateRetailEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(ItemTypeDatTable.RetailCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
