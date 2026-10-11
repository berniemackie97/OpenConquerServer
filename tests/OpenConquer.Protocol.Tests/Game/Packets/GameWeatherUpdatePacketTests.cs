using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameWeatherUpdatePacketTests
{
    [Fact]
    public void Packet_ExposesFields()
    {
        GameWeatherUpdatePacket packet = new(kind: 5, intensity: 700, directionDegrees: 270, parameter: 0x12345678);

        Assert.Equal((ushort)1016, packet.PacketId);
        Assert.Equal(sizeof(uint) * 4, packet.PayloadLength);
        Assert.Equal(5u, packet.Kind);
        Assert.Equal(700u, packet.Intensity);
        Assert.Equal(270u, packet.DirectionDegrees);
        Assert.Equal(0x12345678u, packet.Parameter);
    }

    [Fact]
    public void CreateClear_UsesNoWeatherState()
    {
        GameWeatherUpdatePacket packet = GameWeatherUpdatePacket.CreateClear();

        Assert.Equal(GameWeatherUpdatePacket.ClearKind, packet.Kind);
        Assert.Equal(0u, packet.Intensity);
        Assert.Equal(0u, packet.DirectionDegrees);
        Assert.Equal(0u, packet.Parameter);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesExpectedLayout()
    {
        GameWeatherUpdatePacket packet = new(kind: 5, intensity: 700, directionDegrees: 270, parameter: 0x11121314);
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
    public void GameWireFrameEncoder_WritesClearWeatherFrame()
    {
        GameWeatherUpdatePacket packet = GameWeatherUpdatePacket.CreateClear();
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
            new GameWeatherUpdatePacket(kind, intensity: 0, directionDegrees: 0, parameter: 0));

        Assert.Equal("kind", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsMaximumIntensity()
    {
        GameWeatherUpdatePacket packet = new(kind: 1, GameWeatherUpdatePacket.MaximumIntensity, directionDegrees: 0, parameter: 0);

        Assert.Equal(GameWeatherUpdatePacket.MaximumIntensity, packet.Intensity);
    }

    [Fact]
    public void Constructor_RejectsIntensityAboveMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameWeatherUpdatePacket(kind: 1, GameWeatherUpdatePacket.MaximumIntensity + 1, directionDegrees: 0, parameter: 0));

        Assert.Equal("intensity", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsMaximumDirection()
    {
        GameWeatherUpdatePacket packet = new(kind: 1, intensity: 0, GameWeatherUpdatePacket.MaximumDirectionDegrees, parameter: 0);

        Assert.Equal(GameWeatherUpdatePacket.MaximumDirectionDegrees, packet.DirectionDegrees);
    }

    [Fact]
    public void Constructor_RejectsDirectionAboveMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameWeatherUpdatePacket(kind: 1, intensity: 0, GameWeatherUpdatePacket.MaximumDirectionDegrees + 1, parameter: 0));

        Assert.Equal("directionDegrees", exception.ParamName);
    }
}
