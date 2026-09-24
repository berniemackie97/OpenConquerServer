using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameWeatherUpdatePacket1016Tests
{
    [Fact]
    public void Packet_ExposesVerifiedFields()
    {
        GameWeatherUpdatePacket1016 packet = new(kind: 5, intensity: 700, directionDegrees: 270, parameter: 0x12345678);

        Assert.Equal((ushort)1016, packet.PacketId);
        Assert.Equal(sizeof(uint) * 4, packet.PayloadLength);
        Assert.Equal(5u, packet.Kind);
        Assert.Equal(700u, packet.Intensity);
        Assert.Equal(270u, packet.DirectionDegrees);
        Assert.Equal(0x12345678u, packet.Parameter);
    }

    [Fact]
    public void CreateClear_UsesVerifiedNativeNoWeatherState()
    {
        GameWeatherUpdatePacket1016 packet = GameWeatherUpdatePacket1016.CreateClear();

        Assert.Equal(GameWeatherUpdatePacket1016.ClearKind, packet.Kind);
        Assert.Equal(0u, packet.Intensity);
        Assert.Equal(0u, packet.DirectionDegrees);
        Assert.Equal(0u, packet.Parameter);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedNativeLayout()
    {
        GameWeatherUpdatePacket1016 packet = new(kind: 5, intensity: 700, directionDegrees: 270, parameter: 0x11121314);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x14, 0x00, 0xF8, 0x03,
            0x05, 0x00, 0x00, 0x00,
            0xBC, 0x02, 0x00, 0x00,
            0x0E, 0x01, 0x00, 0x00,
            0x14, 0x13, 0x12, 0x11,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedClearWeatherFrame()
    {
        GameWeatherUpdatePacket1016 packet = GameWeatherUpdatePacket1016.CreateClear();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x14, 0x00, 0xF8, 0x03,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(12)]
    public void Constructor_RejectsUnsupportedWeatherKind(uint kind)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameWeatherUpdatePacket1016(kind, intensity: 0, directionDegrees: 0, parameter: 0));

        Assert.Equal("kind", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsVerifiedMaximumIntensity()
    {
        GameWeatherUpdatePacket1016 packet = new(kind: 1, GameWeatherUpdatePacket1016.MaximumIntensity, directionDegrees: 0, parameter: 0);

        Assert.Equal(GameWeatherUpdatePacket1016.MaximumIntensity, packet.Intensity);
    }

    [Fact]
    public void Constructor_RejectsIntensityAboveVerifiedMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameWeatherUpdatePacket1016(kind: 1, GameWeatherUpdatePacket1016.MaximumIntensity + 1, directionDegrees: 0, parameter: 0));

        Assert.Equal("intensity", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsVerifiedMaximumDirection()
    {
        GameWeatherUpdatePacket1016 packet = new(kind: 1, intensity: 0, GameWeatherUpdatePacket1016.MaximumDirectionDegrees, parameter: 0);

        Assert.Equal(GameWeatherUpdatePacket1016.MaximumDirectionDegrees, packet.DirectionDegrees);
    }

    [Fact]
    public void Constructor_RejectsDirectionAboveVerifiedMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameWeatherUpdatePacket1016(kind: 1, intensity: 0, GameWeatherUpdatePacket1016.MaximumDirectionDegrees + 1, parameter: 0));

        Assert.Equal("directionDegrees", exception.ParamName);
    }
}
