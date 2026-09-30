using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameSocialRelation1019Tests
{
    [Fact]
    public void ActionConstants_MatchVerifiedNativeContract()
    {
        Assert.Equal((byte)10, (byte)GameSocialRelationAction.FriendRequest);
        Assert.Equal((byte)11, (byte)GameSocialRelationAction.AddFriend);
        Assert.Equal((byte)12, (byte)GameSocialRelationAction.FriendOnline);
        Assert.Equal((byte)13, (byte)GameSocialRelationAction.FriendOffline);
        Assert.Equal((byte)14, (byte)GameSocialRelationAction.RemoveFriend);
        Assert.Equal((byte)15, (byte)GameSocialRelationAction.AddFriendSilently);
        Assert.Equal((byte)16, (byte)GameSocialRelationAction.EnemyOnline);
        Assert.Equal((byte)17, (byte)GameSocialRelationAction.EnemyOffline);
        Assert.Equal((byte)18, (byte)GameSocialRelationAction.RemoveEnemy);
        Assert.Equal((byte)19, (byte)GameSocialRelationAction.AddEnemy);
    }

    [Fact]
    public void PacketConstants_MatchVerifiedNativeContract()
    {
        Assert.Equal((ushort)1019, GameSocialRelation1019.PacketIdentifier);
        Assert.Equal(36, GameSocialRelation1019.FixedPacketLength);
        Assert.Equal(32, GameSocialRelation1019.PayloadSize);
        Assert.Equal(16, GameSocialRelation1019.NameFieldLength);
        Assert.Equal(15, GameSocialRelation1019.MaximumNameEncodedLength);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteVerifiedNativeLayout()
    {
        GameSocialRelationPacket1019 packet = new(0x01020304, GameSocialRelationAction.AddFriendSilently, 0x05,
            0x06070809, 0x0A0B0C0D, "Bernie");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x24, 0x00, 0xFB, 0x03,
            0x04, 0x03, 0x02, 0x01,
            0x0F,
            0x05,
            0x00, 0x00,
            0x09, 0x08, 0x07, 0x06,
            0x0D, 0x0C, 0x0B, 0x0A,
            0x42, 0x65, 0x72, 0x6E, 0x69, 0x65,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00,
        ];

        Assert.Equal(GameSocialRelation1019.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesEnemyHydrationAction()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(action: GameSocialRelationAction.AddEnemy);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)19, destination[8]);
    }

    [Fact]
    public void GameWireFrameEncoder_PreservesStateFlagAsRawByte()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(stateFlag: 0xA5);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0xA5, destination[9]);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesReservedField()
    {
        GameSocialRelationPacket1019 packet = CreatePacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x00, 0x00], destination[10..12]);
    }

    [Fact]
    public void GameWireFrameEncoder_ForcesFinalNameByteToNull()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(name: "ABCDEFGHIJKLMNO");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal("ABCDEFGHIJKLMNO"u8.ToArray(), destination[20..35]);
        Assert.Equal((byte)0, destination[35]);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesStrictWindows1252Name()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(name: "€Bernie");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0x80, destination[20]);
        Assert.Equal((byte)'B', destination[21]);
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsNameLongerThanFifteenEncodedBytes()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(name: "ABCDEFGHIJKLMNOP");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Throws<ArgumentOutOfRangeException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsUnrepresentableName()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(name: "漢");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Throws<EncoderFallbackException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsZeroEntityId()
    {
        GameSocialRelationPacket1019 packet = CreatePacket(entityId: 0);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((byte)9)]
    [InlineData((byte)20)]
    [InlineData((byte)255)]
    public void GameWireFrameEncoder_RejectsUndefinedAction(byte action)
    {
        GameSocialRelationPacket1019 packet = CreatePacket(action: (GameSocialRelationAction)action);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("action", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_ParsesVerifiedNativeLayout()
    {
        byte[] packet = BuildPacket();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal(0x01020304u, relation.EntityId);
        Assert.Equal(GameSocialRelationAction.AddFriendSilently, relation.Action);
        Assert.Equal((byte)0x05, relation.StateFlag);
        Assert.Equal((ushort)0, relation.ReservedField);
        Assert.Equal(0x06070809u, relation.PeerageRank);
        Assert.Equal(0x0A0B0C0Du, relation.PeerageSex);
        Assert.Equal("Bernie", relation.Name);
    }

    [Fact]
    public void TryParse_ParsesMaximumLengthName()
    {
        byte[] packet = BuildPacket(name: "ABCDEFGHIJKLMNO");

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal("ABCDEFGHIJKLMNO", relation.Name);
    }

    [Fact]
    public void TryParse_StopsAtEarlierNullWithinNameField()
    {
        byte[] packet = BuildPacket(name: "Bernie");
        packet[27] = (byte)'X';

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal("Bernie", relation.Name);
    }

    [Theory]
    [InlineData(GameSocialRelationAction.FriendRequest)]
    [InlineData(GameSocialRelationAction.AddFriend)]
    [InlineData(GameSocialRelationAction.FriendOnline)]
    [InlineData(GameSocialRelationAction.FriendOffline)]
    [InlineData(GameSocialRelationAction.RemoveFriend)]
    [InlineData(GameSocialRelationAction.AddFriendSilently)]
    [InlineData(GameSocialRelationAction.EnemyOnline)]
    [InlineData(GameSocialRelationAction.EnemyOffline)]
    [InlineData(GameSocialRelationAction.RemoveEnemy)]
    [InlineData(GameSocialRelationAction.AddEnemy)]
    public void TryParse_AcceptsEveryVerifiedAction(GameSocialRelationAction action)
    {
        byte[] packet = BuildPacket(action: action);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal(action, relation.Action);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)9)]
    [InlineData((byte)20)]
    [InlineData((byte)255)]
    public void TryParse_PreservesUnrecognizedActionForCallerValidation(byte action)
    {
        byte[] packet = BuildPacket(action: (GameSocialRelationAction)action);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal(action, (byte)relation.Action);
    }

    [Fact]
    public void TryParse_PreservesReservedFieldForCallerValidation()
    {
        byte[] packet = BuildPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10), 0x1234);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal((ushort)0x1234, relation.ReservedField);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketId()
    {
        byte[] packet = BuildPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1020);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameSocialRelation1019.TryParse(frame, out _, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.InvalidPacketId, error);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(37)]
    [InlineData(40)]
    public void TryParse_RejectsNonNativePacketLength(int length)
    {
        byte[] packet = BuildPacket(length: length);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameSocialRelation1019.TryParse(frame, out _, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.InvalidPacketLength, error);
    }

    [Fact]
    public void TryParse_RejectsMissingFinalNameTerminator()
    {
        byte[] packet = BuildPacket(name: "ABCDEFGHIJKLMNO");
        packet[35] = (byte)'P';

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameSocialRelation1019.TryParse(frame, out _, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.MissingNameTerminator, error);
    }

    [Fact]
    public void TryParse_PreservesWindows1252ByteDomain()
    {
        byte[] packet = BuildPacket();
        packet[20] = 0x81;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameSocialRelation1019.TryParse(frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error));
        Assert.Equal(GameSocialRelationParseError.None, error);
        Assert.Equal('\u0081', relation.Name[0]);
    }

    [Fact]
    public void TryParse_ThrowsForNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => GameSocialRelation1019.TryParse(null!, out _, out _));
    }

    private static GameSocialRelationPacket1019 CreatePacket(uint entityId = 0x01020304,
        GameSocialRelationAction action = GameSocialRelationAction.AddFriendSilently, byte stateFlag = 1,
        uint peerageRank = 7, uint peerageSex = 1, string name = "Bernie")
    {
        return new GameSocialRelationPacket1019(entityId, action, stateFlag, peerageRank, peerageSex, name);
    }

    private static byte[] BuildPacket(int length = GameSocialRelation1019.FixedPacketLength,
        GameSocialRelationAction action = GameSocialRelationAction.AddFriendSilently, string name = "Bernie")
    {
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameSocialRelation1019.PacketIdentifier);

        if (length < GameSocialRelation1019.FixedPacketLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 0x01020304);
        packet[8] = (byte)action;
        packet[9] = 0x05;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 0x06070809);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), 0x0A0B0C0D);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] encodedName = Encoding.GetEncoding(1252).GetBytes(name);
        encodedName.CopyTo(packet, 20);
        packet[35] = 0;

        return packet;
    }

    private static GameInboundFrame CreateFrame(byte[] packet)
    {
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(buffer, 0);

        return new GameInboundFrame(buffer, header);
    }
}
