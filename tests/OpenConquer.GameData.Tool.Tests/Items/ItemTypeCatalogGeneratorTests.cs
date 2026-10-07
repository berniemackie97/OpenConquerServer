using System.Globalization;
using System.Text;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Assets.Items;
using OpenConquer.GameData.Tool.Items;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.GameData.Tool.Tests.Items;

public sealed class ItemTypeCatalogGeneratorTests
{
    private const int SeedTableLength = 128;

    private static readonly Encoding s_textEncoding = CreateTextEncoding();

    [Fact]
    public void Generate_ValidSource_ProducesOrderedCanonicalDefinitions()
    {
        string decodedText = string.Join(
            "\r\n",
            CreateLine(
                CreateFields(
                    itemTypeId: 100002,
                    name: "Second",
                    requiredLevel: 20,
                    speedPercentOffset: -10,
                    life: 200,
                    mana: 50,
                    initialDurability: 300,
                    maximumDurability: 400,
                    staticLifetimeMinutes: 10080,
                    stackCapacity: 20
                )
            ),
            CreateLine(
                CreateFields(
                    itemTypeId: 100001,
                    name: "Delight~of~Speed",
                    requiredLevel: 15,
                    speedPercentOffset: 5,
                    life: 120,
                    mana: 30,
                    initialDurability: 100,
                    maximumDurability: 200,
                    staticLifetimeMinutes: 0,
                    stackCapacity: 0
                )
            )
        );

        ItemTypeDatTable source = ItemTypeDatTable.Parse(EncodeText(decodedText));

        var definitions = ItemTypeCatalogGenerator.Generate(source);

        Assert.Equal(2, definitions.Length);

        Assert.Equal(100001u, definitions[0].ItemTypeId);
        Assert.Equal("Delight of Speed", definitions[0].Name);
        Assert.Equal((byte)15, definitions[0].RequiredLevel);
        Assert.Equal((short)5, definitions[0].SpeedPercentOffset);
        Assert.Equal((short)120, definitions[0].Life);
        Assert.Equal((short)30, definitions[0].Mana);
        Assert.Equal((ushort)100, definitions[0].InitialDurability);
        Assert.Equal((ushort)200, definitions[0].MaximumDurability);
        Assert.Equal(0u, definitions[0].StaticLifetimeMinutes);
        Assert.Equal((ushort)0, definitions[0].StackCapacity);

        Assert.Equal(100002u, definitions[1].ItemTypeId);
        Assert.Equal((ushort)300, definitions[1].InitialDurability);
        Assert.Equal((ushort)400, definitions[1].MaximumDurability);
        Assert.Equal(10080u, definitions[1].StaticLifetimeMinutes);
        Assert.Equal((ushort)20, definitions[1].StackCapacity);
    }

    [Fact]
    public void Generate_NegativeStaticLifetime_ThrowsInvalidDataException()
    {
        string[] fields = CreateFields(100001, "TestItem");
        fields[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex] = "-1";

        ItemTypeDatTable source = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ItemTypeCatalogGenerator.Generate(source)
        );

        Assert.Contains("negative static lifetime", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    public void Generate_InvalidStackCapacity_ThrowsInvalidDataException(string stackCapacity)
    {
        string[] fields = CreateFields(100001, "TestItem");
        fields[ItemTypeDatRecord.StackCapacityFieldIndex] = stackCapacity;

        ItemTypeDatTable source = ItemTypeDatTable.Parse(EncodeText(CreateLine(fields)));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ItemTypeCatalogGenerator.Generate(source)
        );

        Assert.Contains(
            "outside the supported unsigned 16-bit range",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Write_GeneratedCatalog_RoundTripsThroughRuntimeLoader()
    {
        ItemTypeDatTable source = ItemTypeDatTable.Parse(
            EncodeText(
                CreateLine(
                    CreateFields(
                        itemTypeId: 100001,
                        name: "TestItem",
                        requiredLevel: 15,
                        speedPercentOffset: -5,
                        life: 120,
                        mana: 30,
                        initialDurability: 100,
                        maximumDurability: 200,
                        staticLifetimeMinutes: 10080,
                        stackCapacity: 20
                    )
                )
            )
        );

        var definitions = ItemTypeCatalogGenerator.Generate(source);

        using TemporaryDirectory directory = new();
        string catalogPath = Path.Combine(directory.RootPath, "item-types.json");

        ItemTypeCatalogFileWriter.Write(catalogPath, definitions);

        Assert.Equal(
            FileItemTypeCatalogRepository.FormatVersion,
            ItemTypeCatalogFileWriter.FormatVersion
        );

        FileItemTypeCatalogRepository repository = new(
            new ItemTypeCatalogFileOptions(
                catalogPath,
                maximumFileLengthBytes: 1024 * 1024,
                maximumDefinitions: 100
            )
        );

        ItemTypeCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, catalog.Count);
        Assert.True(catalog.TryGet(100001, out var definition));
        Assert.Equal("TestItem", definition.Name);
        Assert.Equal((ushort)100, definition.InitialDurability);
        Assert.Equal((ushort)200, definition.MaximumDurability);
        Assert.Equal((ushort)20, definition.StackCapacity);
    }

    [Fact]
    public void Write_SameDefinitions_ProducesIdenticalBytes()
    {
        ItemTypeDatTable source = ItemTypeDatTable.Parse(
            EncodeText(
                string.Join(
                    "\r\n",
                    CreateLine(CreateFields(100002, "Second")),
                    CreateLine(CreateFields(100001, "First"))
                )
            )
        );

        var definitions = ItemTypeCatalogGenerator.Generate(source);

        using TemporaryDirectory directory = new();

        string firstPath = Path.Combine(directory.RootPath, "first.json");
        string secondPath = Path.Combine(directory.RootPath, "second.json");

        ItemTypeCatalogFileWriter.Write(firstPath, definitions);
        ItemTypeCatalogFileWriter.Write(secondPath, definitions);

        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
    }

    private static string[] CreateFields(
        uint itemTypeId,
        string name,
        byte requiredLevel = 0,
        short speedPercentOffset = 0,
        short life = 0,
        short mana = 0,
        ushort initialDurability = 1,
        ushort maximumDurability = 1,
        int staticLifetimeMinutes = 0,
        int stackCapacity = 0
    )
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.RecordFieldCount).ToArray();

        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = itemTypeId.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.NameFieldIndex] = name;
        fields[ItemTypeDatRecord.RequiredLevelFieldIndex] = requiredLevel.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.SpeedPercentOffsetFieldIndex] = speedPercentOffset.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.LifeFieldIndex] = life.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.ManaFieldIndex] = mana.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.InitialDurabilityFieldIndex] = initialDurability.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.MaximumDurabilityFieldIndex] = maximumDurability.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex] = staticLifetimeMinutes.ToString(
            CultureInfo.InvariantCulture
        );
        fields[ItemTypeDatRecord.StackCapacityFieldIndex] = stackCapacity.ToString(
            CultureInfo.InvariantCulture
        );

        return fields;
    }

    private static string CreateLine(string[] fields)
    {
        return string.Join("@@", fields) + "@@";
    }

    private static byte[] EncodeText(string decodedText)
    {
        byte[] encodedPayload = s_textEncoding.GetBytes(decodedText);

        Span<byte> seedTable = stackalloc byte[SeedTableLength];
        BuildSeedTable(seedTable, ItemTypeDatTable.DecodedTextSeed);

        for (int index = 0; index < encodedPayload.Length; index++)
        {
            int rotation = index & 7;
            byte transformed =
                rotation == 0 ? encodedPayload[index] : RotateLeft(encodedPayload[index], rotation);

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

    private static Encoding CreateTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(
            ItemTypeDatTable.TextCodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback
        );
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
