using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameMapInfoPacket1110Tests
{
    [Fact]
    public void Packet_ExposesVerifiedFields()
    {
        GameMapInfoPacket1110 packet = new(0x01020304, 0x05060708, 0x1112131415161718UL);

        Assert.Equal((ushort)1110, packet.PacketId);
        Assert.Equal(sizeof(uint) * 4, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.MapId);
        Assert.Equal(0x05060708u, packet.MapDataId);
        Assert.Equal(0x1112131415161718UL, packet.Flags);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedNativeLayout()
    {
        GameMapInfoPacket1110 packet = new(0x01020304, 0x05060708, 0x1112131415161718UL);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x14, 0x00, 0x56, 0x04,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05,
            0x18, 0x17, 0x16, 0x15,
            0x14, 0x13, 0x12, 0x11,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_PreservesDistinctMapAndMapDataIdentifiers()
    {
        GameMapInfoPacket1110 packet = new(mapId: 1002, mapDataId: 1015, flags: 0);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(
            [
                0x14, 0x00, 0x56, 0x04,
                0xEA, 0x03, 0x00, 0x00,
                0xF7, 0x03, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            ],
            destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesFlagsLowDwordBeforeHighDword()
    {
        GameMapInfoPacket1110 packet = new(mapId: 1, mapDataId: 2, flags: 0xA1A2A3A4B1B2B3B4UL);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(
            [
                0xB4, 0xB3, 0xB2, 0xB1,
                0xA4, 0xA3, 0xA2, 0xA1,
            ],
            destination.AsSpan(12, 8).ToArray());
    }
}
