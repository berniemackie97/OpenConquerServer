using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GamePositionReportTests
{
    [Theory]
    [InlineData(GamePositionReport.IdleReportAction)]
    [InlineData(GamePositionReport.CorrectionReplyAction)]
    public void TryParse_DecodesNativePositionPairAtWireOffsetEight(ushort action)
    {
        using GameInboundFrame frame = CreateFrame(action);

        Assert.True(GamePositionReport.TryParse(frame, out GamePositionReport report));
        Assert.Equal(0x11223344u, report.EntityId);
        Assert.Equal((ushort)700, report.X);
        Assert.Equal((ushort)1200, report.Y);
        Assert.Equal(action, report.Action);
    }

    [Theory]
    [InlineData((ushort)0x4A, (uint)0x11223344, (byte)0, 38)]
    [InlineData((ushort)108, (uint)0x11223344, (byte)0, 38)]
    [InlineData((ushort)100, 0u, (byte)0, 38)]
    [InlineData((ushort)146, (uint)0x11223344, (byte)1, 38)]
    [InlineData((ushort)100, (uint)0x11223344, (byte)0, 39)]
    public void TryParse_RejectsUnsupportedActionIdentityOrVariableLength(ushort action, uint entity, byte strings, int length)
    {
        using GameInboundFrame frame = CreateFrame(action, entity, strings, length);
        Assert.False(GamePositionReport.TryParse(frame, out _));
    }

    [Fact]
    public void TryParse_RejectsWrongPacketId()
    {
        using GameInboundFrame frame = CreateFrame(GamePositionReport.IdleReportAction, packetId: 10005);
        Assert.False(GamePositionReport.TryParse(frame, out _));
    }

    [Fact]
    public void Correction108_WritesAuthoritativeTileToNativeCorrectionFields()
    {
        GameActionPacket correction = new(0x11223344, parameterPair: 0, actionParameter: 0,
            timestamp: 55, action: 108, direction: 0, positionX: 700,
            positionY: 1200, data1: 0, data2: 0, flag: 0);
        byte[] bytes = new byte[GameWireFrameEncoder.GetFrameLength(correction)];
        GameWireFrameEncoder.WriteFrame(correction, bytes);

        Assert.Equal(GameAction.FixedPacketLength, bytes.Length);
        Assert.Equal((ushort)108, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal((ushort)700, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24)));
        Assert.Equal((ushort)1200, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)));
    }

    private static GameInboundFrame CreateFrame(ushort action, uint entityId = 0x11223344,
        byte strings = 0, int length = GameAction.FixedPacketLength, ushort packetId = GameAction.PacketIdentifier)
    {
        byte[] packet = new byte[length];
        WireFrameHeader.Write(packet, checked((ushort)length), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), (1200u << 16) | 700);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), action);
        packet[37] = strings;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        packet.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
