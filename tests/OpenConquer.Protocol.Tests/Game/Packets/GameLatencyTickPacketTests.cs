using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameLatencyTickPacketTests
{
    [Fact]
    public void TryParse_AcceptsNativeHeartbeat_AndEchoesItByteForByte()
    {
        byte[] wire = CreateHeartbeat();
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.True(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? packet));
        Assert.Equal(GameLatencyTickPacket.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameLatencyTickPacket.FixedPacketLength - WireFrameHeader.Size, packet.PayloadLength);

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameLatencyTickPacket.FixedPacketLength, written);
        Assert.Equal(wire, destination);
    }

    [Fact]
    public void TryParse_EchoesTheRecordedClientHeartbeatUnchanged()
    {
        byte[] wire = new byte[GameLatencyTickPacket.FixedPacketLength];
        BinaryPrimitives.WriteUInt16LittleEndian(wire, GameLatencyTickPacket.FixedPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), GameLatencyTickPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), 1_000_000);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), GameLatencyTickPacket.LatencyTickSubtype);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(16), 68_952_244);

        using GameInboundFrame frame = CreateFrame(wire);
        Assert.True(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? tick));

        byte[] echo = new byte[GameWireFrameEncoder.GetFrameLength(tick)];
        Assert.Equal(wire.Length, GameWireFrameEncoder.WriteFrame(tick, echo));
        Assert.Equal(wire, echo);
        Assert.Equal("AA2D5B328C0F90B9B80EEA69F1E648EE33653FC9092FF633D72E4C9E05D4C922",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(echo)));
    }

    [Fact]
    public void TryParse_CopiesPayload_SoTheEchoOutlivesTheReceiveBuffer()
    {
        byte[] wire = CreateHeartbeat();
        GameLatencyTickPacket? packet;
        using (GameInboundFrame frame = CreateFrame(wire))
        {
            Assert.True(GameLatencyTickPacket.TryParse(frame, out packet));
        }

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(wire, destination);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)26)]
    [InlineData((ushort)28)]
    [InlineData((ushort)46)]
    public void TryParse_RejectsOtherItemActionSubtypes(ushort subtype)
    {
        byte[] wire = CreateHeartbeat();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), subtype);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? packet));
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_RejectsOtherPacketTypes()
    {
        byte[] wire = CreateHeartbeat();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), 1010);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out _));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(84)]
    [InlineData(92)]
    public void TryParse_RejectsLengthsOtherThanTheNativeFrame(int length)
    {
        byte[] wire = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(wire, (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), GameLatencyTickPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), GameLatencyTickPacket.LatencyTickSubtype);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out _));
    }

    [Fact]
    public void TryParse_RejectsNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => GameLatencyTickPacket.TryParse(null!, out _));
    }

    private static byte[] CreateHeartbeat()
    {
        // Fill unrelated payload bytes to verify the echo preserves them without interpretation.
        byte[] wire = new byte[GameLatencyTickPacket.FixedPacketLength];
        for (int index = WireFrameHeader.Size; index < wire.Length; index++)
        {
            wire[index] = (byte)(index * 7);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(wire, GameLatencyTickPacket.FixedPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), GameLatencyTickPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), 1000001u);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), GameLatencyTickPacket.LatencyTickSubtype);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(16), 0x0012D687u);
        return wire;
    }

    private static GameInboundFrame CreateFrame(byte[] packet)
    {
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(buffer, 0);

        return new GameInboundFrame(buffer, header);
    }
}
