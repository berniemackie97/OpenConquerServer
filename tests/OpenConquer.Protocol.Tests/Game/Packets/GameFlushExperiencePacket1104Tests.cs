using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameFlushExperiencePacket1104Tests
{
    [Fact]
    public void Packet_ExposesCanonicalVerified5517Contract()
    {
        GameFlushExperiencePacket1104 packet = new(0x01020304, 0x05060708, 0x090A, GameFlushExperiencePacket1104.MagicAction);

        Assert.Equal((ushort)1104, GameFlushExperiencePacket1104.PacketIdentifier);
        Assert.Equal(16, GameFlushExperiencePacket1104.CanonicalPacketLength);
        Assert.Equal(12, GameFlushExperiencePacket1104.PayloadSize);
        Assert.Equal((byte)0, GameFlushExperiencePacket1104.WeaponSkillAction);
        Assert.Equal((byte)1, GameFlushExperiencePacket1104.MagicAction);
        Assert.Equal(GameFlushExperiencePacket1104.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameFlushExperiencePacket1104.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.Experience);
        Assert.Equal(0x05060708u, packet.NextLevelExperienceRequirement);
        Assert.Equal((ushort)0x090A, packet.SkillOrMagicType);
        Assert.Equal(GameFlushExperiencePacket1104.MagicAction, packet.Action);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCanonicalSixteenByteLayout()
    {
        GameFlushExperiencePacket1104 packet = new(0x01020304, 0x05060708, 0x090A, GameFlushExperiencePacket1104.MagicAction);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x10, 0x00, 0x50, 0x04,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05,
            0x0A, 0x09,
            0x01,
            0x00,
        ];

        Assert.Equal(GameFlushExperiencePacket1104.CanonicalPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesCanonicalTailPadding()
    {
        GameFlushExperiencePacket1104 packet = new(123456, 654321, 1000, GameFlushExperiencePacket1104.MagicAction);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0, destination[0x0F]);
    }

    [Theory]
    [InlineData(GameFlushExperiencePacket1104.WeaponSkillAction)]
    [InlineData(GameFlushExperiencePacket1104.MagicAction)]
    public void GameWireFrameEncoder_PreservesVerifiedKnownActions(byte action)
    {
        GameFlushExperiencePacket1104 packet = new(1, 2, 3, action);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(action, destination[0x0E]);
    }
}
