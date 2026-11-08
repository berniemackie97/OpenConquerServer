using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenConquer.Protocol.Tests.Game.Packets;

internal static class WireGoldenFixture
{
    private const string EvidencePrefix = "20_notes/Network/evidence/typed-world-dispatch-2026-11-06/wire-layout-golden.json#";
    private const string SourceImageHash = "b6ba5ab8573ec073ea271f57b16fa14de1c67bfb44efd5aa7cfbfc0d3b488a62";

    internal static byte[] ReadFrame(string fileName, ushort packetId, string expectedSha256)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "WireGolden", fileName);
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement source = fixture.RootElement;

        Assert.Equal(1, source.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("static-derived", source.GetProperty("classification").GetString());
        Assert.False(source.GetProperty("runtimeCaptured").GetBoolean());
        Assert.Equal("game", source.GetProperty("protocol").GetString());
        Assert.Equal("server-to-client", source.GetProperty("direction").GetString());
        Assert.Equal(packetId, source.GetProperty("packetId").GetUInt16());
        Assert.Equal(SourceImageHash, source.GetProperty("referenceImageSha256").GetString());
        Assert.StartsWith(EvidencePrefix, source.GetProperty("sourceEvidence").GetString()!);

        byte[] frame = Convert.FromHexString(source.GetProperty("frameHex").GetString()!);
        Assert.Equal(expectedSha256, source.GetProperty("frameSha256").GetString());
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(frame)).ToLowerInvariant());
        Assert.True(frame.Length >= 4);
        Assert.Equal(frame.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.Equal(packetId, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
        return frame;
    }
}
