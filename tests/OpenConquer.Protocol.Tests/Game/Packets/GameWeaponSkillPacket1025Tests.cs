using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameWeaponSkillPacket1025Tests
{
    [Fact]
    public void Packet_ExposesCompleteVerifiedWireContract()
    {
        GameWeaponSkillPacket1025 packet = new(0x01020304, 0x0506, 0x0708090A, 0x0B0C0D0E);

        Assert.Equal((ushort)1025, GameWeaponSkillPacket1025.PacketIdentifier);
        Assert.Equal(20, GameWeaponSkillPacket1025.FixedPacketLength);
        Assert.Equal(16, GameWeaponSkillPacket1025.PayloadSize);
        Assert.Equal(GameWeaponSkillPacket1025.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameWeaponSkillPacket1025.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.WeaponSkillType);
        Assert.Equal((ushort)0x0506, packet.Level);
        Assert.Equal(0x0708090Au, packet.Experience);
        Assert.Equal(0x0B0C0D0Eu, packet.NextLevelExperienceRequirement);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteVerifiedNativeLayout()
    {
        GameWeaponSkillPacket1025 packet = new(0x01020304, 0x0506, 0x0708090A, 0x0B0C0D0E);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x14, 0x00, 0x01, 0x04,
            0x04, 0x03, 0x02, 0x01,
            0x06, 0x05,
            0x00, 0x00,
            0x0A, 0x09, 0x08, 0x07,
            0x0E, 0x0D, 0x0C, 0x0B,
        ];

        Assert.Equal(GameWeaponSkillPacket1025.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesVerifiedReservedField()
    {
        GameWeaponSkillPacket1025 packet = new(410, 20, 123456, 654321);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x00, 0x00], destination[0x0A..0x0C]);
    }
}
