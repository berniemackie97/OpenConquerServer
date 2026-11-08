using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class WireGoldenWeather1016Tests
{
    [Fact]
    public void WeatherEncoder_MatchesPinnedStaticDerivedPacket()
    {
        byte[] expected = WireGoldenFixture.ReadFrame(
            "1016-weather.static-derived.json", GameWeatherUpdatePacket.PacketIdentifier, "27925e3dd6c73095eca525c2c03f12449fd3d0894f4a1c06ce6628c3f89d53ee");

        GameWeatherUpdatePacket packet = new(2, 1022, 359, 0xfedcba98);
        byte[] actual = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Equal(expected.Length, GameWireFrameEncoder.WriteFrame(packet, actual));
        Assert.Equal(expected, actual);
    }
}
