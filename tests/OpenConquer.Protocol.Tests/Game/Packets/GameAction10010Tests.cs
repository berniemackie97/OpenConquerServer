using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameAction10010Tests
{
    [Fact]
    public void TryParse_ParsesVerifiedFixedNativeLayout()
    {
        byte[] packet = BuildPacket();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameAction10010.TryParse(frame, out GameAction10010 action, out GameActionParseError error));
        Assert.Equal(GameActionParseError.None, error);
        Assert.Equal(0x01020304u, action.EntityId);
        Assert.Equal(0x05060708u, action.ParameterPair);
        Assert.Equal(0x090A0B0Cu, action.ActionParameter);
        Assert.Equal(0x0D0E0F10u, action.Timestamp);
        Assert.Equal((ushort)0x1112, action.Action);
        Assert.Equal((ushort)0x1314, action.Direction);
        Assert.Equal((ushort)0x1516, action.PositionX);
        Assert.Equal((ushort)0x1718, action.PositionY);
        Assert.Equal(0x191A1B1Cu, action.Data1);
        Assert.Equal(0x1D1E1F20u, action.Data2);
        Assert.Equal((byte)0x21, action.Flag);
        Assert.Equal((byte)0, action.StringCount);
    }

    [Fact]
    public void TryParse_AllowsTrailingNativeStringMaterial()
    {
        byte[] packet = BuildPacket(length: 41, stringCount: 1);
        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameAction10010.TryParse(frame, out GameAction10010 action, out GameActionParseError error));
        Assert.Equal(GameActionParseError.None, error);
        Assert.Equal((byte)1, action.StringCount);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketId()
    {
        byte[] packet = BuildPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameAction10010.TryParse(frame, out _, out GameActionParseError error));
        Assert.Equal(GameActionParseError.InvalidPacketId, error);
    }

    [Fact]
    public void TryParse_RejectsTruncatedFixedBody()
    {
        byte[] packet = BuildPacket(length: GameAction10010.FixedPacketLength - 1);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameAction10010.TryParse(frame, out _, out GameActionParseError error));
        Assert.Equal(GameActionParseError.TruncatedBody, error);
    }

    [Fact]
    public void TryParse_ThrowsForNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => GameAction10010.TryParse(null!, out _, out _));
    }

    [Fact]
    public void CreateEnterMapAcknowledgement_MapsVerifiedFields()
    {
        GameActionPacket10010 packet = GameActionPacket10010.CreateEnterMapAcknowledgement(0x01020304, 0x05060708, 0x0910, 0x1112, 0x13141516);

        Assert.Equal(GameAction10010.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameActionPacket10010.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.EntityId);
        Assert.Equal(0x05060708u, packet.ParameterPair);
        Assert.Equal(0u, packet.ActionParameter);
        Assert.Equal(0x13141516u, packet.Timestamp);
        Assert.Equal(GameAction10010.EnterMapAction, packet.Action);
        Assert.Equal((ushort)0, packet.Direction);
        Assert.Equal((ushort)0x0910, packet.PositionX);
        Assert.Equal((ushort)0x1112, packet.PositionY);
        Assert.Equal(0u, packet.Data1);
        Assert.Equal(0u, packet.Data2);
        Assert.Equal((byte)0, packet.Flag);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedFixedLayout()
    {
        GameActionPacket10010 packet = new(0x01020304, 0x05060708, 0x090A0B0C, 0x0D0E0F10, 0x1112, 0x1314, 0x1516, 0x1718, 0x191A1B1C, 0x1D1E1F20, 0x21);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x26, 0x00, 0x1A, 0x27,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05,
            0x0C, 0x0B, 0x0A, 0x09,
            0x10, 0x0F, 0x0E, 0x0D,
            0x12, 0x11,
            0x14, 0x13,
            0x16, 0x15,
            0x18, 0x17,
            0x1C, 0x1B, 0x1A, 0x19,
            0x20, 0x1F, 0x1E, 0x1D,
            0x21, 0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Theory]
    [InlineData(0u, 1u, "entityId")]
    [InlineData(1u, 0u, "mapDataId")]
    public void CreateEnterMapAcknowledgement_RejectsZeroRequiredIdentity(uint entityId, uint mapDataId, string parameterName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameActionPacket10010.CreateEnterMapAcknowledgement(entityId, mapDataId, 100, 200, 300));

        Assert.Equal(parameterName, exception.ParamName);
    }

    private static byte[] BuildPacket(int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
    {
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameAction10010.PacketIdentifier);

        if (length < GameAction10010.FixedPacketLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 0x01020304);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0x05060708);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 0x090A0B0C);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), 0x0D0E0F10);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), 0x1112);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22), 0x1314);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(24), 0x1516);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26), 0x1718);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), 0x191A1B1C);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), 0x1D1E1F20);
        packet[36] = 0x21;
        packet[37] = stringCount;

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
