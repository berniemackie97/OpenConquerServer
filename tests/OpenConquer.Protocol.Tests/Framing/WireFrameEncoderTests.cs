using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Tests.Framing;

public sealed class WireFrameEncoderTests
{
    [Fact]
    public void GetFrameLength_ReturnsHeaderAndPayloadLength()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: [0xAA, 0xBB, 0xCC]);

        int length = WireFrameEncoder.GetFrameLength(packet);

        Assert.Equal(7, length);
    }

    [Fact]
    public void GetFrameLength_AllowsZeroLengthPayload()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        int length = WireFrameEncoder.GetFrameLength(packet);

        Assert.Equal(WireFrameHeader.Size, length);
    }

    [Fact]
    public void GetFrameLength_RejectsZeroPacketId()
    {
        IPacket packet = new TestPacket(packetId: 0, payload: []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.GetFrameLength(packet)
        );

        Assert.Equal("Packet identifier 0 is invalid.", exception.Message);
    }

    [Fact]
    public void GetFrameLength_AllowsMaximumWireFrameLength()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: ushort.MaxValue - WireFrameHeader.Size,
            payload: []
        );

        int length = WireFrameEncoder.GetFrameLength(packet);

        Assert.Equal(ushort.MaxValue, length);
    }

    [Fact]
    public void GetFrameLength_RejectsNegativePayloadLength()
    {
        IPacket packet = new DeclaredLengthPacket(packetId: 0x1234, payloadLength: -1, payload: []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.GetFrameLength(packet)
        );

        Assert.Equal("Packet 4660 declared a negative payload length.", exception.Message);
    }

    [Fact]
    public void GetFrameLength_RejectsFrameAboveWireMaximum()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: ushort.MaxValue - WireFrameHeader.Size + 1,
            payload: []
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.GetFrameLength(packet)
        );

        Assert.Equal(
            "Packet 4660 declares a 65536-byte frame, which exceeds the 65535-byte maximum.",
            exception.Message
        );
    }

    [Fact]
    public void GetFrameLength_HandlesIntMaxPayloadWithoutOverflowing()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: int.MaxValue,
            payload: []
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.GetFrameLength(packet)
        );

        Assert.Equal(
            "Packet 4660 declares a 2147483651-byte frame, which exceeds the 65535-byte maximum.",
            exception.Message
        );
    }

    [Fact]
    public void GetFrameLength_EnforcesCallerMaximum()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: 1021,
            payload: []
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.GetFrameLength(packet, maximumFrameLength: 0x400)
        );

        Assert.Equal(
            "Packet 4660 declares a 1025-byte frame, which exceeds the 1024-byte maximum.",
            exception.Message
        );
    }

    [Fact]
    public void GetFrameLength_AllowsFrameExactlyAtCallerMaximum()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: 0x400 - WireFrameHeader.Size,
            payload: []
        );

        int length = WireFrameEncoder.GetFrameLength(packet, maximumFrameLength: 0x400);

        Assert.Equal(0x400, length);
    }

    [Fact]
    public void GetFrameLength_RejectsMaximumBelowHeaderSize()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WireFrameEncoder.GetFrameLength(packet, WireFrameHeader.Size - 1)
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    [Fact]
    public void GetFrameLength_RejectsMaximumAboveWireMaximum()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WireFrameEncoder.GetFrameLength(packet, ushort.MaxValue + 1)
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    [Fact]
    public void GetFrameLength_ThrowsForNullPacket()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            WireFrameEncoder.GetFrameLength(null!)
        );

        Assert.Equal("packet", exception.ParamName);
    }

    [Fact]
    public void WriteFrame_WritesExactHeaderAndPayload()
    {
        IPacket packet = new TestPacket(packetId: 0x5678, payload: [0xAA, 0xBB]);

        Span<byte> destination = stackalloc byte[6];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(6, written);

        Assert.Equal([0x06, 0x00, 0x78, 0x56, 0xAA, 0xBB], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_WritesZeroPayloadFrame()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(WireFrameHeader.Size, written);

        Assert.Equal([0x04, 0x00, 0x34, 0x12], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_RejectsZeroPacketIdWithoutModifyingDestination()
    {
        IPacket packet = new TestPacket(packetId: 0, payload: [0xAA]);

        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WireFrameEncoder.WriteFrame(packet, destination)
        );

        Assert.Equal("Packet identifier 0 is invalid.", exception.Message);

        Assert.Equal([0xCC, 0xCC, 0xCC, 0xCC, 0xCC], destination);
    }

    [Fact]
    public void WriteFrame_LeavesBytesBeyondFrameUntouched()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: [0xAA]);

        Span<byte> destination = stackalloc byte[8];
        destination.Fill(0xCC);

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(5, written);

        Assert.Equal([0x05, 0x00, 0x34, 0x12, 0xAA, 0xCC, 0xCC, 0xCC], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_AllowsMaximumWireFrame()
    {
        int payloadLength = ushort.MaxValue - WireFrameHeader.Size;

        byte[] payload = new byte[payloadLength];

        payload[0] = 0xAA;
        payload[^1] = 0xBB;

        IPacket packet = new TestPacket(packetId: 0x1234, payload: payload);

        byte[] destination = new byte[ushort.MaxValue];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(ushort.MaxValue, written);

        Assert.True(WireFrameHeader.TryRead(destination, out WireFrameHeader header));

        Assert.Equal(ushort.MaxValue, header.Length);
        Assert.Equal(0x1234, header.PacketId);

        Assert.Equal(0xAA, destination[WireFrameHeader.Size]);

        Assert.Equal(0xBB, destination[^1]);
    }

    [Fact]
    public void WriteFrame_AllowsFrameExactlyAtCallerMaximum()
    {
        int payloadLength = 0x400 - WireFrameHeader.Size;

        byte[] payload = new byte[payloadLength];

        IPacket packet = new TestPacket(packetId: 0x1234, payload: payload);

        byte[] destination = new byte[0x400];

        int written = WireFrameEncoder.WriteFrame(packet, destination, maximumFrameLength: 0x400);

        Assert.Equal(0x400, written);
    }

    [Fact]
    public void WriteFrame_RejectsFrameAboveCallerMaximumWithoutModifyingDestination()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: 1021,
            payload: []
        );

        Span<byte> destination = stackalloc byte[1025];
        destination.Fill(0xCC);

        try
        {
            WireFrameEncoder.WriteFrame(packet, destination, maximumFrameLength: 0x400);

            Assert.Fail("Expected the frame to exceed the configured maximum.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "Packet 4660 declares a 1025-byte frame, which exceeds the 1024-byte maximum.",
                exception.Message
            );
        }

        Assert.All(destination.ToArray(), value => Assert.Equal(0xCC, value));
    }

    [Fact]
    public void WriteFrame_RejectsDestinationSmallerThanFrameWithoutModifyingDestination()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: [0xAA, 0xBB]);

        Span<byte> destination = stackalloc byte[5];
        destination.Fill(0xCC);

        try
        {
            WireFrameEncoder.WriteFrame(packet, destination);

            Assert.Fail("Expected the destination to be too small.");
        }
        catch (ArgumentException exception)
        {
            Assert.Equal("destination", exception.ParamName);

            Assert.StartsWith("Destination must contain at least 6 bytes.", exception.Message);
        }

        Assert.All(destination.ToArray(), value => Assert.Equal(0xCC, value));
    }

    [Fact]
    public void WriteFrame_ClearsFrameWhenPacketWritesTooFewBytes()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: 2,
            payload: [0xAA]
        );

        Span<byte> destination = stackalloc byte[8];
        destination.Fill(0xCC);

        try
        {
            WireFrameEncoder.WriteFrame(packet, destination);

            Assert.Fail("Expected the payload length mismatch to be rejected.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "Packet 4660 declared a payload length of 2 bytes but wrote 1 bytes.",
                exception.Message
            );
        }

        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xCC, 0xCC], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_ClearsFrameWhenPacketWritesTooManyBytes()
    {
        IPacket packet = new DeclaredLengthPacket(
            packetId: 0x1234,
            payloadLength: 1,
            payload: [0xAA, 0xBB]
        );

        Span<byte> destination = stackalloc byte[7];
        destination.Fill(0xCC);

        try
        {
            WireFrameEncoder.WriteFrame(packet, destination);

            Assert.Fail("Expected the packet to exceed its declared payload capacity.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 2 bytes with 1 remaining.",
                exception.Message
            );
        }

        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x00, 0xCC, 0xCC], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_ClearsFrameWhenPacketSerializationThrows()
    {
        IPacket packet = new ThrowingPacket(packetId: 0x1234, payloadLength: 2);

        Span<byte> destination = stackalloc byte[8];
        destination.Fill(0xCC);

        try
        {
            WireFrameEncoder.WriteFrame(packet, destination);

            Assert.Fail("Expected packet serialization to throw.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("Packet serialization failed.", exception.Message);
        }

        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xCC, 0xCC], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_DoesNotWriteHeaderBeforePayloadSucceeds()
    {
        byte[] destination = Enumerable.Repeat((byte)0xCC, 5).ToArray();
        IPacket packet = new HeaderObservingPacket(destination);

        WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x05, 0x00, 0x34, 0x12, 0xAA], destination);
    }

    [Fact]
    public void WriteFrame_SnapshotsPacketMetadataOnce()
    {
        MetadataChangingPacket packet = new();

        Span<byte> destination = stackalloc byte[5];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(5, written);
        Assert.Equal(1, packet.PacketIdReadCount);
        Assert.Equal(1, packet.PayloadLengthReadCount);

        Assert.Equal([0x05, 0x00, 0x34, 0x12, 0xAA], destination.ToArray());
    }

    [Fact]
    public void WriteFrame_ThrowsForNullPacket()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(WriteNullPacket);

        Assert.Equal("packet", exception.ParamName);
    }

    [Fact]
    public void WriteFrame_RejectsMaximumBelowHeaderSize()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            WriteWithMaximumBelowHeader
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    [Fact]
    public void WriteFrame_RejectsMaximumAboveWireMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            WriteWithMaximumAboveWireLimit
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    private static void WriteNullPacket()
    {
        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameEncoder.WriteFrame(null!, destination);
    }

    private static void WriteWithMaximumBelowHeader()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameEncoder.WriteFrame(packet, destination, WireFrameHeader.Size - 1);
    }

    private static void WriteWithMaximumAboveWireLimit()
    {
        IPacket packet = new TestPacket(packetId: 0x1234, payload: []);

        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameEncoder.WriteFrame(packet, destination, ushort.MaxValue + 1);
    }

    private sealed class TestPacket(ushort packetId, byte[] payload) : IPacket
    {
        public ushort PacketId => packetId;

        public int PayloadLength => payload.Length;

        public void WritePayload(ref PacketWriter writer)
        {
            writer.WriteBytes(payload);
        }
    }

    private sealed class DeclaredLengthPacket(ushort packetId, int payloadLength, byte[] payload)
        : IPacket
    {
        public ushort PacketId => packetId;

        public int PayloadLength => payloadLength;

        public void WritePayload(ref PacketWriter writer)
        {
            writer.WriteBytes(payload);
        }
    }

    private sealed class ThrowingPacket(ushort packetId, int payloadLength) : IPacket
    {
        public ushort PacketId => packetId;

        public int PayloadLength => payloadLength;

        public void WritePayload(ref PacketWriter writer)
        {
            writer.WriteByte(0xAA);

            throw new InvalidOperationException("Packet serialization failed.");
        }
    }

    private sealed class HeaderObservingPacket(byte[] destination) : IPacket
    {
        public ushort PacketId => 0x1234;

        public int PayloadLength => 1;

        public void WritePayload(ref PacketWriter writer)
        {
            Assert.All(destination[..WireFrameHeader.Size], value => Assert.Equal(0xCC, value));

            writer.WriteByte(0xAA);
        }
    }

    private sealed class MetadataChangingPacket : IPacket
    {
        public int PacketIdReadCount { get; private set; }

        public int PayloadLengthReadCount { get; private set; }

        public ushort PacketId
        {
            get
            {
                PacketIdReadCount++;

                return PacketIdReadCount == 1 ? (ushort)0x1234 : (ushort)0xFFFF;
            }
        }

        public int PayloadLength
        {
            get
            {
                PayloadLengthReadCount++;

                return PayloadLengthReadCount == 1 ? 1 : 500;
            }
        }

        public void WritePayload(ref PacketWriter writer)
        {
            writer.WriteByte(0xAA);
        }
    }
}
