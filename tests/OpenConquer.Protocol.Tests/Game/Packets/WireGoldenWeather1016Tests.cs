using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class WireGoldenWeather1016Tests
{
    private const string FrameHash = "27925e3dd6c73095eca525c2c03f12449fd3d0894f4a1c06ce6628c3f89d53ee";

    [Fact]
    public void WeatherEncoder_MatchesPinnedStaticDerivedPacket()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "WireGolden", "1016-weather.static-derived.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement source = fixture.RootElement;

        Assert.Equal(1, source.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("static-derived", source.GetProperty("classification").GetString());
        Assert.False(source.GetProperty("runtimeCaptured").GetBoolean());
        Assert.Equal("game", source.GetProperty("protocol").GetString());
        Assert.Equal("server-to-client", source.GetProperty("direction").GetString());
        Assert.Equal(1016, source.GetProperty("packetId").GetInt32());
        Assert.Equal(FrameHash, source.GetProperty("frameSha256").GetString());

        byte[] expected = Convert.FromHexString(source.GetProperty("frameHex").GetString()!);
        Assert.Equal(FrameHash, Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());

        GameWeatherUpdatePacket packet = new(2, 1022, 359, 0xfedcba98);
        byte[] actual = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        Assert.Equal(expected.Length, GameWireFrameEncoder.WriteFrame(packet, actual));
        Assert.Equal(expected, actual);
    }
}
