using System.Security.Cryptography;
using System.Text.Json;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class WireGoldenCorpusManifestTests
{
    private const string ManifestHash = "ad0080adb7730c1235afa411cd25dc84eeb2f47c1630d43d1cf4bf7b89a373a1";

    [Fact]
    public void ManifestPinsEveryCheckedInWireFixture()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "WireGolden");
        byte[] manifest = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
        Assert.Equal(ManifestHash, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant());

        using JsonDocument document = JsonDocument.Parse(manifest);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("berniemackie97/OpenConquerClient", root.GetProperty("sourceRepository").GetString());
        Assert.Equal("e13bf5724975ec825e64a3609719afedd9b6af41", root.GetProperty("sourceCommit").GetString());

        List<string> listedFiles = [];
        foreach (JsonElement entry in root.GetProperty("cases").EnumerateArray())
        {
            string fileName = entry.GetProperty("file").GetString()!;
            Assert.Equal(Path.GetFileName(fileName), fileName);
            listedFiles.Add(fileName);

            byte[] fixture = File.ReadAllBytes(Path.Combine(directory, fileName));
            Assert.Equal(entry.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant());
        }

        Assert.Equal(Directory.EnumerateFiles(directory, "*.json")
            .Select(Path.GetFileName)
            .Where(static name => name != "manifest.json")
            .OrderBy(static name => name), listedFiles.OrderBy(static name => name));
    }
}
