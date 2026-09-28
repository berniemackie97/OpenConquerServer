using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameLoginHistoryPacket2078Tests
{
    [Fact]
    public void Packet_ExposesVerifiedIdentifierAndFields()
    {
        GameLoginHistoryPacket2078 packet = new(0x1234_5678, 1, "Miami");

        Assert.Equal((ushort)2078, packet.PacketId);
        Assert.Equal(11, packet.PayloadLength);
        Assert.Equal(0x1234_5678u, packet.LastLoginTimestamp);
        Assert.Equal((byte)1, packet.LocationWarningFlag);
        Assert.Equal("Miami", packet.LastLoginLocation);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesKnownCompatibleEmptyLoginHistoryFrame()
    {
        GameLoginHistoryPacket2078 packet = new(0, 0, "");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x0A, 0x00, 0x1E, 0x08,
            0x00, 0x00, 0x00, 0x00,
            0x00,
            0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedLoginHistoryLayout()
    {
        GameLoginHistoryPacket2078 packet = new(0x1234_5678, 1, "Miami");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x0F, 0x00, 0x1E, 0x08,
            0x78, 0x56, 0x34, 0x12,
            0x01,
            (byte)'M', (byte)'i', (byte)'a', (byte)'m', (byte)'i', 0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void Packet_UsesEncodedLocationByteLength()
    {
        GameLoginHistoryPacket2078 packet = new(0, 0, "€");

        Assert.Equal(7, packet.PayloadLength);
    }

    [Fact]
    public void Constructor_RejectsNullLocation()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new GameLoginHistoryPacket2078(0, 0, null!));

        Assert.Equal("lastLoginLocation", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsEmbeddedNullLocation()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new GameLoginHistoryPacket2078(0, 0, "Miami\0Florida"));

        Assert.Equal("lastLoginLocation", exception.ParamName);
        Assert.StartsWith("Last-login location must not contain embedded null characters.", exception.Message);
    }
}
