using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Domain.Items;
using OpenConquer.GameData.Tool.Formats.Items;
using OpenConquer.GameData.Tool.Items;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.GameData.Tool.Tests.Items;

public sealed class ItemTypeCatalogGeneratorTests
{
    private const int SeedTableLength = 128;
    private static readonly Encoding s_textEncoding = CreateTextEncoding();
    private static readonly string s_hash = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]));

    [Fact]
    public void Generate_ValidSource_ProducesOrderedCanonicalDefinitions()
    {
        string[] first = Fields(100001, "Delight~of~Speed", requiredLevel: 15, speedPercentOffset: 5,
            life: 120, mana: 30, initialDurability: 100, maximumDurability: 200);
        first[2] = "55";
        first[10] = "1024";
        first[12] = "123456";
        first[32] = "5";
        first[33] = "900";
        first[37] = "888";
        string[] second = Fields(100002, "Second", requiredLevel: 20, speedPercentOffset: -10,
            life: 200, mana: 50, initialDurability: 300, maximumDurability: 400,
            staticLifetimeMinutes: 10080, stackCapacity: 20);

        ItemTypeDefinition[] definitions = Generate(second, first);
        Assert.Equal(2, definitions.Length);
        ItemTypeDefinition definition = definitions[0];
        Assert.Equal(100001u, definition.ItemTypeId);
        Assert.Equal("Delight of Speed", definition.Name);
        Assert.Equal((byte)15, definition.RequiredLevel);
        Assert.Equal((short)5, definition.SpeedPercentOffset);
        Assert.Equal((short)120, definition.Life);
        Assert.Equal((short)30, definition.Mana);
        Assert.Equal((ushort)100, definition.InitialDurability);
        Assert.Equal((ushort)200, definition.MaximumDurability);
        Assert.Equal(0u, definition.StaticLifetimeMinutes);
        Assert.Equal((ushort)0, definition.StackCapacity);
        Assert.Equal(55, definition.RequiredProfessionCode);
        Assert.Equal(1024u, definition.MonopolyFlags);
        Assert.Equal(123456u, definition.SilverPrice);
        Assert.Equal(5, definition.AttackRange);
        Assert.Equal(900, definition.AttackSpeed);
        Assert.Equal(888u, definition.ConquerPointsPrice);
        Assert.Equal(100002u, definitions[1].ItemTypeId);
        Assert.Equal((ushort)300, definitions[1].InitialDurability);
        Assert.Equal((ushort)400, definitions[1].MaximumDurability);
        Assert.Equal(10080u, definitions[1].StaticLifetimeMinutes);
        Assert.Equal((ushort)20, definitions[1].StackCapacity);
    }

    [Fact]
    public void Generate_MaximumRepresentableStaticLifetime_IsAccepted()
    {
        int lifetime = checked((int)ItemTypeDefinition.MaximumStaticLifetimeMinutes);
        Assert.Equal(ItemTypeDefinition.MaximumStaticLifetimeMinutes,
            Assert.Single(Generate(Fields(100001, "TestItem", staticLifetimeMinutes: lifetime))).StaticLifetimeMinutes);
    }

    [Fact]
    public void Generate_NegativeStaticLifetime_ThrowsInvalidDataException()
    {
        string[] fields = Fields(100001, "TestItem");
        fields[39] = "-1";
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Generate(fields));
        Assert.Contains("negative static lifetime", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_StaticLifetimeAboveRepresentableMaximum_ThrowsInvalidDataException()
    {
        int lifetime = checked((int)ItemTypeDefinition.MaximumStaticLifetimeMinutes + 1);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() =>
            Generate(Fields(100001, "TestItem", staticLifetimeMinutes: lifetime)));
        Assert.Contains("exceeding the supported maximum", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    public void Generate_InvalidStackCapacity_ThrowsInvalidDataException(string value)
    {
        string[] fields = Fields(100001, "TestItem");
        fields[47] = value;
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Generate(fields));
        Assert.Contains("outside the supported unsigned 16-bit range", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, "invalid")]
    [InlineData(10, "-1")]
    [InlineData(12, "4294967296")]
    [InlineData(32, "2147483648")]
    [InlineData(33, "unknown")]
    [InlineData(37, "-1")]
    public void Generate_InvalidExtendedNativeField_ThrowsWithFieldNumber(int index, string value)
    {
        string[] fields = Fields(100001, "TestItem");
        fields[index] = value;
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Generate(fields));
        Assert.Contains($"at native field {index}", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_GeneratedCatalog_RoundTripsThroughRuntimeLoader()
    {
        string[] fields = Fields(100001, "TestItem", requiredLevel: 15, speedPercentOffset: -5,
            life: 120, mana: 30, initialDurability: 100, maximumDurability: 200,
            staticLifetimeMinutes: 10080, stackCapacity: 20);
        fields[2] = "135";
        fields[12] = "50000";
        fields[37] = "200";
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.RootPath, "item-types.json");
        ItemTypeCatalogFileWriter.Write(path, Generate(fields), s_hash);
        Assert.Equal(FileItemTypeCatalogRepository.FormatVersion, ItemTypeCatalogFileWriter.FormatVersion);
        FileItemTypeCatalogRepository repo = new(new ItemTypeCatalogFileOptions(path, 1024 * 1024, 100));
        ItemTypeCatalog catalog = await repo.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(catalog.TryGet(100001, out ItemTypeDefinition? item));
        Assert.Equal("TestItem", item.Name);
        Assert.Equal((ushort)100, item.InitialDurability);
        Assert.Equal((ushort)200, item.MaximumDurability);
        Assert.Equal((ushort)20, item.StackCapacity);
        Assert.Equal(135, item.RequiredProfessionCode);
        Assert.Equal(50000u, item.SilverPrice);
        Assert.Equal(200u, item.ConquerPointsPrice);
    }

    [Fact]
    public void Write_SameDefinitionsInDifferentOrders_ProducesIdenticalBytes()
    {
        ItemTypeDefinition[] definitions = Generate(Fields(100002, "Second"), Fields(100001, "First"));
        using TemporaryDirectory directory = new();
        string a = Path.Combine(directory.RootPath, "a.json");
        string b = Path.Combine(directory.RootPath, "b.json");
        ItemTypeCatalogFileWriter.Write(a, definitions, s_hash);
        ItemTypeCatalogFileWriter.Write(b, definitions.Reverse().ToArray(), s_hash);
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }

    [Fact]
    public void Write_EmptyCatalog_ThrowsBeforeCreatingOutput()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.RootPath, "item-types.json");
        Assert.Throws<ArgumentException>(() => ItemTypeCatalogFileWriter.Write(path, [], s_hash));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_NullDefinition_ThrowsBeforeCreatingOutput()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.RootPath, "item-types.json");
        Assert.Throws<ArgumentException>(() => ItemTypeCatalogFileWriter.Write(path, [null!], s_hash));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_DuplicateItemTypeId_DoesNotReplaceExistingOutput()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.RootPath, "item-types.json");
        File.WriteAllText(path, "existing");
        ItemTypeDefinition[] definitions = Generate(Fields(100001, "First"));
        Assert.Throws<ArgumentException>(() => ItemTypeCatalogFileWriter.Write(path, [definitions[0], definitions[0]], s_hash));
        Assert.Equal("existing", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha256")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Write_InvalidHash_DoesNotReplaceExistingOutput(string hash)
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.RootPath, "item-types.json");
        File.WriteAllText(path, "existing");
        Assert.Throws<ArgumentException>(() => ItemTypeCatalogFileWriter.Write(path, Generate(Fields(100001, "First")), hash));
        Assert.Equal("existing", File.ReadAllText(path));
    }

    private static ItemTypeDefinition[] Generate(params string[][] sourceFields)
    {
        string text = string.Join("\r\n", sourceFields.Select(x => string.Join("@@", x) + "@@"));
        return ItemTypeCatalogGenerator.Generate(ItemTypeDatTable.Parse(EncodeText(text)));
    }

    private static string[] Fields(uint itemTypeId, string name, byte requiredLevel = 0,
        short speedPercentOffset = 0, short life = 0, short mana = 0, ushort initialDurability = 1,
        ushort maximumDurability = 1, int staticLifetimeMinutes = 0, int stackCapacity = 0)
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.RecordFieldCount).ToArray();
        fields[0] = itemTypeId.ToString(CultureInfo.InvariantCulture);
        fields[1] = name;
        fields[4] = requiredLevel.ToString(CultureInfo.InvariantCulture);
        fields[18] = speedPercentOffset.ToString(CultureInfo.InvariantCulture);
        fields[19] = life.ToString(CultureInfo.InvariantCulture);
        fields[20] = mana.ToString(CultureInfo.InvariantCulture);
        fields[21] = initialDurability.ToString(CultureInfo.InvariantCulture);
        fields[22] = maximumDurability.ToString(CultureInfo.InvariantCulture);
        fields[39] = staticLifetimeMinutes.ToString(CultureInfo.InvariantCulture);
        fields[47] = stackCapacity.ToString(CultureInfo.InvariantCulture);
        return fields;
    }

    private static byte[] EncodeText(string text)
    {
        byte[] data = s_textEncoding.GetBytes(text);
        Span<byte> seedTable = stackalloc byte[SeedTableLength];
        uint state = unchecked((uint)ItemTypeDatTable.DecodedTextSeed);
        for (int i = 0; i < seedTable.Length; i++)
        {
            state = unchecked(state * 214013u + 2531011u);
            seedTable[i] = (byte)(((state >> 16) & 0x7FFFu) % 256u);
        }
        for (int i = 0; i < data.Length; i++)
        {
            int rotate = i & 7;
            byte transformed = rotate == 0 ? data[i] : (byte)((data[i] << rotate) | (data[i] >> (8 - rotate)));
            data[i] = (byte)(transformed ^ seedTable[i % SeedTableLength]);
        }
        return data;
    }

    private static Encoding CreateTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(ItemTypeDatTable.TextCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }
        public string RootPath { get; }
        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
