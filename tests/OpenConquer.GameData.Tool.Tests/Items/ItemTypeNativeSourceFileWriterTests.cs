using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.GameData.Tool.Formats.Items;
using OpenConquer.GameData.Tool.Items;

namespace OpenConquer.GameData.Tool.Tests.Items;

public sealed class ItemTypeNativeSourceFileWriterTests
{
    [Fact]
    public void Write_PreservesEveryNativeFieldInOriginalOrderAndPinsSourceHash()
    {
        byte[] source = [0, 1, 2, 3, 4, 255];
        string[] fields = CreateFields(100001, "青虹剑");
        fields[2] = "55";
        fields[10] = "7";
        fields[12] = "100000";
        fields[18] = "-15";
        fields[37] = "250";
        fields[53] = "Weapon~Description";
        fields[54] = "物品描述";
        fields[58] = "";

        ItemTypeDatRecord record = CreateRecord(100001, fields);
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "source.json");

        ItemTypeNativeSourceFileWriter.Write(path, [record], source);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("itemtype.dat-cp936-59-fields", root.GetProperty("sourceFormat").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(source)), root.GetProperty("sourceSha256").GetString());
        Assert.Equal(59, root.GetProperty("fieldCount").GetInt32());
        JsonElement entry = Assert.Single(root.GetProperty("itemTypes").EnumerateArray());
        Assert.Equal(100001u, entry.GetProperty("itemTypeId").GetUInt32());
        Assert.Equal(fields, entry.GetProperty("fields").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public void Write_OrdersRecordsByNumericIdentityAndIsByteDeterministic()
    {
        ItemTypeDatRecord first = CreateRecord(100001, CreateFields(100001, "First"));
        ItemTypeDatRecord second = CreateRecord(100002, CreateFields(100002, "Second"));
        byte[] source = [9, 8, 7];
        using TemporaryDirectory directory = new();
        string pathA = Path.Combine(directory.Path, "a.json");
        string pathB = Path.Combine(directory.Path, "b.json");

        ItemTypeNativeSourceFileWriter.Write(pathA, [second, first], source);
        ItemTypeNativeSourceFileWriter.Write(pathB, [first, second], source);

        Assert.Equal(File.ReadAllBytes(pathA), File.ReadAllBytes(pathB));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(pathA));
        Assert.Equal([100001u, 100002u], document.RootElement.GetProperty("itemTypes")
            .EnumerateArray().Select(value => value.GetProperty("itemTypeId").GetUInt32()).ToArray());
    }

    [Fact]
    public void Write_DuplicateIdsRejectBeforeReplacingExistingOutput()
    {
        ItemTypeDatRecord record = CreateRecord(100001, CreateFields(100001, "First"));
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "source.json");
        File.WriteAllText(path, "original");

        Assert.Throws<InvalidDataException>(() =>
            ItemTypeNativeSourceFileWriter.Write(path, [record, record], [1]));

        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void Write_NullRecordIsRejectedBeforeCreatingOutput()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "source.json");

        Assert.Throws<InvalidDataException>(() =>
            ItemTypeNativeSourceFileWriter.Write(path, [null!], [1]));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_InconsistentEncodedIdRejectsBeforeReplacingExistingOutput()
    {
        ItemTypeDatRecord record = CreateRecord(100001, CreateFields(100002, "WrongId"));
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "source.json");
        File.WriteAllText(path, "original");

        Assert.Throws<InvalidDataException>(() =>
            ItemTypeNativeSourceFileWriter.Write(path, [record], [1]));

        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void Write_EmptyRecordsAndSourceAreRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "source.json");
        ItemTypeDatRecord record = CreateRecord(100001, CreateFields(100001, "First"));

        Assert.Throws<ArgumentException>(() => ItemTypeNativeSourceFileWriter.Write(path, [], [1]));
        Assert.Throws<ArgumentException>(() => ItemTypeNativeSourceFileWriter.Write(path, [record], []));
        Assert.False(File.Exists(path));
    }

    private static string[] CreateFields(uint itemTypeId, string name)
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.RecordFieldCount).ToArray();
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = itemTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.NameFieldIndex] = name;
        return fields;
    }

    private static ItemTypeDatRecord CreateRecord(uint itemTypeId, string[] fields)
    {
        return new ItemTypeDatRecord(itemTypeId, fields, 0, 0, 0, 0, 1, 1, 0, 1);
    }
}
