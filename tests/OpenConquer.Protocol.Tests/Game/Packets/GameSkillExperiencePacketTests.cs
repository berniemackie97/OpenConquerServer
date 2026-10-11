using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameSkillExperiencePacketTests
{
    [Fact]
    public void Packet_ExposesCompleteContract()
    {
        GameSkillExperiencePacket packet = new(0x01020304, 0x05060708, 0x090A, GameSkillExperiencePacket.MagicAction);

        Assert.Equal((ushort)1104, GameSkillExperiencePacket.PacketIdentifier);
        Assert.Equal(16, GameSkillExperiencePacket.FixedPacketLength);
        Assert.Equal(12, GameSkillExperiencePacket.PayloadSize);
        Assert.Equal((byte)0, GameSkillExperiencePacket.WeaponSkillAction);
        Assert.Equal((byte)1, GameSkillExperiencePacket.MagicAction);
        Assert.Equal(GameSkillExperiencePacket.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameSkillExperiencePacket.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.Experience);
        Assert.Equal(0x05060708u, packet.NextLevelExperienceRequirement);
        Assert.Equal((ushort)0x090A, packet.SkillOrMagicType);
        Assert.Equal(GameSkillExperiencePacket.MagicAction, packet.Action);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCanonicalSixteenByteLayout()
    {
        GameSkillExperiencePacket packet = new(0x01020304, 0x05060708, 0x090A, GameSkillExperiencePacket.MagicAction);
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

        Assert.Equal(GameSkillExperiencePacket.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesCanonicalTailPadding()
    {
        GameSkillExperiencePacket packet = new(123456, 654321, 1000, GameSkillExperiencePacket.MagicAction);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0, destination[0x0F]);
    }

    [Theory]
    [InlineData(GameSkillExperiencePacket.WeaponSkillAction)]
    [InlineData(GameSkillExperiencePacket.MagicAction)]
    public void GameWireFrameEncoder_PreservesKnownActions(byte action)
    {
        GameSkillExperiencePacket packet = new(1, 2, 3, action);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(action, destination[0x0E]);
    }
}
