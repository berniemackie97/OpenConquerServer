using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameSyndicateAttributeInfoPacket1106Tests
{
    [Fact]
    public void Packet_ExposesCompleteCompatible5517Contract()
    {
        GameSyndicateAttributeInfoPacket1106 packet = CreatePacket();

        Assert.Equal((ushort)1106, GameSyndicateAttributeInfoPacket1106.PacketIdentifier);
        Assert.Equal(92, GameSyndicateAttributeInfoPacket1106.FixedPacketLength);
        Assert.Equal(88, GameSyndicateAttributeInfoPacket1106.PayloadSize);
        Assert.Equal(16, GameSyndicateAttributeInfoPacket1106.LeaderNameFieldLength);
        Assert.Equal(15, GameSyndicateAttributeInfoPacket1106.MaximumLeaderNameEncodedLength);
        Assert.Equal(17, GameSyndicateAttributeInfoPacket1106.SyndicateNameFieldLength);
        Assert.Equal(16, GameSyndicateAttributeInfoPacket1106.MaximumSyndicateNameEncodedLength);

        Assert.Equal(GameSyndicateAttributeInfoPacket1106.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameSyndicateAttributeInfoPacket1106.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.SyndicateId);
        Assert.Equal(0x05060708u, packet.MemberProffer);
        Assert.Equal(0x090A0B0C0D0E0F10ul, packet.SilverFund);
        Assert.Equal(0x11121314u, packet.EmoneyFund);
        Assert.Equal(0x15161718u, packet.Population);
        Assert.Equal(0x191A1B1Cu, packet.MemberRank);
        Assert.Equal("Lead€r", packet.LeaderName);
        Assert.Equal(0x21222324u, packet.RequiredLevel);
        Assert.Equal(0x25262728u, packet.RequiredMetempsychosis);
        Assert.Equal(0x292A2B2Cu, packet.RequiredProfession);
        Assert.Equal((byte)0x2D, packet.SyndicateLevel);
        Assert.Equal((ushort)0x2E2F, packet.Mantle);
        Assert.Equal(0x30313233u, packet.PositionExpirationDate);
        Assert.Equal(0x34353637u, packet.JoinDate);
        Assert.Equal("Syn€Name", packet.SyndicateName);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteCompatible5517Layout()
    {
        GameSyndicateAttributeInfoPacket1106 packet = CreatePacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameSyndicateAttributeInfoPacket1106.FixedPacketLength, written);
        Assert.Equal((ushort)92, BinaryPrimitives.ReadUInt16LittleEndian(destination));
        Assert.Equal(GameSyndicateAttributeInfoPacket1106.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(destination.AsSpan(2)));

        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(4)));
        Assert.Equal(0x05060708u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(8)));
        Assert.Equal(0x090A0B0C0D0E0F10ul, BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(12)));
        Assert.Equal(0x11121314u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(20)));
        Assert.Equal(0x15161718u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(24)));
        Assert.Equal(0x191A1B1Cu, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(28)));

        Assert.Equal(new byte[] { 0x4C, 0x65, 0x61, 0x64, 0x80, 0x72 }, destination.AsSpan(32, 6).ToArray());
        Assert.All(destination[38..48], value => Assert.Equal((byte)0, value));

        Assert.Equal(0x21222324u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(48)));
        Assert.Equal(0x25262728u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(52)));
        Assert.Equal(0x292A2B2Cu, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(56)));
        Assert.Equal((byte)0x2D, destination[60]);
        Assert.Equal((ushort)0x2E2F, BinaryPrimitives.ReadUInt16LittleEndian(destination.AsSpan(61)));
        Assert.Equal(0x30313233u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(63)));
        Assert.Equal(0x34353637u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(67)));

        Assert.All(destination[71..75], value => Assert.Equal((byte)0, value));

        Assert.Equal(new byte[] { 0x53, 0x79, 0x6E, 0x80, 0x4E, 0x61, 0x6D, 0x65 }, destination.AsSpan(75, 8).ToArray());
        Assert.All(destination[83..92], value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void GameWireFrameEncoder_AllowsZeroSyndicateIdForNativeClearState()
    {
        GameSyndicateAttributeInfoPacket1106 packet = new(syndicateId: 0, memberProffer: 0, silverFund: 0, emoneyFund: 0, population: 0, memberRank: 0, leaderName: string.Empty, requiredLevel: 0, requiredMetempsychosis: 0, requiredProfession: 0, syndicateLevel: 0, mantle: 0, positionExpirationDate: 0, joinDate: 0, syndicateName: string.Empty);

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameSyndicateAttributeInfoPacket1106.FixedPacketLength, written);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(destination.AsSpan(4)));
        Assert.All(destination[4..], value => Assert.Equal((byte)0, value));
    }

    [Theory]
    [InlineData("1234567890123456", "")]
    [InlineData("", "12345678901234567")]
    public void GameWireFrameEncoder_RejectsNamesWithoutRequiredNullTerminatorSpace(string leaderName, string syndicateName)
    {
        GameSyndicateAttributeInfoPacket1106 packet = new(syndicateId: 1, memberProffer: 0, silverFund: 0, emoneyFund: 0, population: 0, memberRank: 0, leaderName, requiredLevel: 0, requiredMetempsychosis: 0, requiredProfession: 0, syndicateLevel: 0, mantle: 0, positionExpirationDate: 0, joinDate: 0, syndicateName);

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsEmbeddedNullName()
    {
        GameSyndicateAttributeInfoPacket1106 packet = new(syndicateId: 1, memberProffer: 0, silverFund: 0, emoneyFund: 0, population: 0, memberRank: 0, leaderName: "Lead\0er", requiredLevel: 0, requiredMetempsychosis: 0, requiredProfession: 0, syndicateLevel: 0, mantle: 0, positionExpirationDate: 0, joinDate: 0, syndicateName: "Syndicate");

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Throws<ArgumentException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsTextOutsideStrictWindows1252()
    {
        GameSyndicateAttributeInfoPacket1106 packet = new(syndicateId: 1, memberProffer: 0, silverFund: 0, emoneyFund: 0, population: 0, memberRank: 0, leaderName: "漢", requiredLevel: 0, requiredMetempsychosis: 0, requiredProfession: 0, syndicateLevel: 0, mantle: 0, positionExpirationDate: 0, joinDate: 0, syndicateName: "Syndicate");

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Throws<EncoderFallbackException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));
    }

    private static GameSyndicateAttributeInfoPacket1106 CreatePacket()
    {
        return new GameSyndicateAttributeInfoPacket1106(syndicateId: 0x01020304, memberProffer: 0x05060708, silverFund: 0x090A0B0C0D0E0F10, emoneyFund: 0x11121314, population: 0x15161718, memberRank: 0x191A1B1C, leaderName: "Lead€r", requiredLevel: 0x21222324, requiredMetempsychosis: 0x25262728, requiredProfession: 0x292A2B2C, syndicateLevel: 0x2D, mantle: 0x2E2F, positionExpirationDate: 0x30313233, joinDate: 0x34353637, syndicateName: "Syn€Name");
    }
}
