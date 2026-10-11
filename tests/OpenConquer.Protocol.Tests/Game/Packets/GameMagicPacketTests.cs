using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameMagicPacketTests
{
    [Fact]
    public void Packet_ExposesCompleteContract()
    {
        GameMagicPacket packet = new(0x01020304, 0x0506, 0x0708);

        Assert.Equal((ushort)1103, GameMagicPacket.PacketIdentifier);
        Assert.Equal(12, GameMagicPacket.FixedPacketLength);
        Assert.Equal(8, GameMagicPacket.PayloadSize);
        Assert.Equal(GameMagicPacket.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameMagicPacket.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.OwnerCharacterId);
        Assert.Equal((ushort)0x0506, packet.MagicType);
        Assert.Equal((ushort)0x0708, packet.Level);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteLayout()
    {
        GameMagicPacket packet = new(0x01020304, 0x0506, 0x0708);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x0C, 0x00, 0x4F, 0x04,
            0x04, 0x03, 0x02, 0x01,
            0x06, 0x05,
            0x08, 0x07,
        ];

        Assert.Equal(GameMagicPacket.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }
}
