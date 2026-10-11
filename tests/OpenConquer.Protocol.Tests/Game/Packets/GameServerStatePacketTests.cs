using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameServerStatePacketTests
{
    [Fact]
    public void Packet_ExposesIdentifierAndFields()
    {
        GameServerStatePacket packet = new(0x1234_5678);

        Assert.Equal((ushort)2079, packet.PacketId);
        Assert.Equal(sizeof(uint), packet.PayloadLength);
        Assert.Equal(0x1234_5678u, packet.State);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesKnownCompatibleInitialStateFrame()
    {
        GameServerStatePacket packet = new(0);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x08, 0x00, 0x1F, 0x08,
            0x00, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesParserAcceptedUnknownStateWithoutExtraFields()
    {
        GameServerStatePacket packet = new(0x1234_5678);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x08, 0x00, 0x1F, 0x08,
            0x78, 0x56, 0x34, 0x12,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }
}
