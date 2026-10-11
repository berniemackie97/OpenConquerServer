using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameWeaponSkillPacketTests
{
    [Fact]
    public void Packet_ExposesCompleteWireContract()
    {
        GameWeaponSkillPacket packet = new(0x01020304, 0x0506, 0x0708090A, 0x0B0C0D0E);

        Assert.Equal((ushort)1025, GameWeaponSkillPacket.PacketIdentifier);
        Assert.Equal(20, GameWeaponSkillPacket.FixedPacketLength);
        Assert.Equal(16, GameWeaponSkillPacket.PayloadSize);
        Assert.Equal(GameWeaponSkillPacket.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameWeaponSkillPacket.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.WeaponSkillType);
        Assert.Equal((ushort)0x0506, packet.Level);
        Assert.Equal(0x0708090Au, packet.Experience);
        Assert.Equal(0x0B0C0D0Eu, packet.NextLevelExperienceRequirement);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteLayout()
    {
        GameWeaponSkillPacket packet = new(0x01020304, 0x0506, 0x0708090A, 0x0B0C0D0E);
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

        Assert.Equal(GameWeaponSkillPacket.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesReservedField()
    {
        GameWeaponSkillPacket packet = new(410, 20, 123456, 654321);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x00, 0x00], destination[0x0A..0x0C]);
    }
}
