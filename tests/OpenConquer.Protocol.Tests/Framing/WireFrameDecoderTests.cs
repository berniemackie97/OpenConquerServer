using System.Buffers;
using OpenConquer.Protocol.Framing;

namespace OpenConquer.Protocol.Tests.Framing;

public sealed class WireFrameDecoderTests
{
    [Fact]
    public void Decode_ReturnsCompleteFrameAndHeader()
    {
        ReadOnlySequence<byte> source = new(new byte[] { 0x06, 0x00, 0x78, 0x56, 0xAA, 0xBB });

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(6, header.Length);
        Assert.Equal(0x5678, header.PacketId);
        Assert.Equal([0x06, 0x00, 0x78, 0x56, 0xAA, 0xBB], frame.ToArray());
    }

    [Fact]
    public void Decode_AllowsHeaderOnlyFrame()
    {
        ReadOnlySequence<byte> source = new(new byte[] { 0x04, 0x00, 0x34, 0x12 });

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(WireFrameHeader.Size, header.Length);
        Assert.Equal(0x1234, header.PacketId);
        Assert.Equal([0x04, 0x00, 0x34, 0x12], frame.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decode_ReturnsIncompleteHeaderUntilFourBytesAreAvailable(int sourceLength)
    {
        ReadOnlySequence<byte> source = new(new byte[sourceLength]);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.IncompleteHeader, status);
        Assert.Equal(default, header);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void Decode_ReturnsIncompleteFrameWhenDeclaredBytesAreMissing()
    {
        ReadOnlySequence<byte> source = new(new byte[] { 0x06, 0x00, 0x34, 0x12, 0xAA });

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.IncompleteFrame, status);
        Assert.Equal(6, header.Length);
        Assert.Equal(0x1234, header.PacketId);
        Assert.True(frame.IsEmpty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decode_RejectsDeclaredLengthSmallerThanHeader(int declaredLength)
    {
        byte[] source = new byte[WireFrameHeader.Size];

        WireFrameHeader.Write(source, (ushort)declaredLength, packetId: 0x1234);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.InvalidFrameLength, status);
        Assert.Equal(declaredLength, header.Length);
        Assert.Equal(0x1234, header.PacketId);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void Decode_ReturnsIncompleteFrameBeforeRejectingZeroPacketId()
    {
        byte[] source = new byte[WireFrameHeader.Size];

        WireFrameHeader.Write(
            source,
            length: 6,
            packetId: 0
        );

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.IncompleteFrame, status);
        Assert.Equal(6, header.Length);
        Assert.Equal(0, header.PacketId);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void Decode_RejectsZeroPacketId()
    {
        byte[] source = new byte[WireFrameHeader.Size];

        WireFrameHeader.Write(source, length: WireFrameHeader.Size, packetId: 0);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.InvalidPacketId, status);
        Assert.Equal(WireFrameHeader.Size, header.Length);
        Assert.Equal(0, header.PacketId);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void Decode_RejectsFrameAboveCallerMaximumBeforeWaitingForPayload()
    {
        byte[] source = new byte[WireFrameHeader.Size];

        WireFrameHeader.Write(source, length: 0x401, packetId: 0x1234);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            maximumFrameLength: 0x400,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.InvalidFrameLength, status);
        Assert.Equal(0x401, header.Length);
        Assert.Equal(0x1234, header.PacketId);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void Decode_AllowsFrameExactlyAtCallerMaximum()
    {
        byte[] source = new byte[0x400];

        WireFrameHeader.Write(source, length: 0x400, packetId: 0x1234);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            maximumFrameLength: 0x400,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(0x400, header.Length);
        Assert.Equal(0x400L, frame.Length);
    }

    [Fact]
    public void Decode_AllowsMaximumRepresentableFrameByDefault()
    {
        byte[] source = new byte[ushort.MaxValue];

        WireFrameHeader.Write(source, length: ushort.MaxValue, packetId: 0x1234);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            new ReadOnlySequence<byte>(source),
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(ushort.MaxValue, header.Length);
        Assert.Equal((long)ushort.MaxValue, frame.Length);
    }

    [Fact]
    public void Decode_ReturnsOnlyFirstFrameFromCoalescedInput()
    {
        byte[] source = [0x05, 0x00, 0x34, 0x12, 0xAA, 0x04, 0x00, 0x78, 0x56];

        ReadOnlySequence<byte> buffered = new(source);

        WireFrameDecodeStatus firstStatus = WireFrameDecoder.Decode(
            buffered,
            out WireFrameHeader firstHeader,
            out ReadOnlySequence<byte> firstFrame
        );

        WireFrameDecodeStatus secondStatus = WireFrameDecoder.Decode(
            buffered.Slice(firstFrame.End),
            out WireFrameHeader secondHeader,
            out ReadOnlySequence<byte> secondFrame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, firstStatus);
        Assert.Equal(5, firstHeader.Length);
        Assert.Equal(0x1234, firstHeader.PacketId);
        Assert.Equal([0x05, 0x00, 0x34, 0x12, 0xAA], firstFrame.ToArray());

        Assert.Equal(WireFrameDecodeStatus.Success, secondStatus);
        Assert.Equal(4, secondHeader.Length);
        Assert.Equal(0x5678, secondHeader.PacketId);
        Assert.Equal([0x04, 0x00, 0x78, 0x56], secondFrame.ToArray());
    }

    [Fact]
    public void Decode_ReadsHeaderSplitAcrossSegments()
    {
        ReadOnlySequence<byte> source = CreateSequence([0x06], [0x00, 0x78], [0x56, 0xAA, 0xBB]);

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(6, header.Length);
        Assert.Equal(0x5678, header.PacketId);
        Assert.Equal([0x06, 0x00, 0x78, 0x56, 0xAA, 0xBB], frame.ToArray());
    }

    [Fact]
    public void Decode_ReturnsFrameAcrossPayloadSegmentsWithoutCoalescingSource()
    {
        ReadOnlySequence<byte> source = CreateSequence(
            [0x07, 0x00, 0x34, 0x12, 0xAA],
            [0xBB],
            [0xCC, 0x7F]
        );

        WireFrameDecodeStatus status = WireFrameDecoder.Decode(
            source,
            out WireFrameHeader header,
            out ReadOnlySequence<byte> frame
        );

        Assert.Equal(WireFrameDecodeStatus.Success, status);
        Assert.Equal(7, header.Length);
        Assert.Equal(0x1234, header.PacketId);
        Assert.False(frame.IsSingleSegment);
        Assert.Equal([0x07, 0x00, 0x34, 0x12, 0xAA, 0xBB, 0xCC], frame.ToArray());
    }

    [Fact]
    public void Decode_RejectsMaximumBelowHeaderSize()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            DecodeWithMaximumBelowHeaderSize
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    [Fact]
    public void Decode_RejectsMaximumAboveWireMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            DecodeWithMaximumAboveWireMaximum
        );

        Assert.Equal("maximumFrameLength", exception.ParamName);
    }

    private static void DecodeWithMaximumBelowHeaderSize()
    {
        _ = WireFrameDecoder.Decode(
            ReadOnlySequence<byte>.Empty,
            WireFrameHeader.Size - 1,
            out _,
            out _
        );
    }

    private static void DecodeWithMaximumAboveWireMaximum()
    {
        _ = WireFrameDecoder.Decode(
            ReadOnlySequence<byte>.Empty,
            ushort.MaxValue + 1,
            out _,
            out _
        );
    }

    private static ReadOnlySequence<byte> CreateSequence(params byte[][] segments)
    {
        if (segments.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        TestSequenceSegment first = new(segments[0]);
        TestSequenceSegment last = first;

        for (int index = 1; index < segments.Length; index++)
        {
            last = last.Append(segments[index]);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class TestSequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public TestSequenceSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public TestSequenceSegment Append(ReadOnlyMemory<byte> memory)
        {
            TestSequenceSegment next = new(memory) { RunningIndex = RunningIndex + Memory.Length };

            Next = next;

            return next;
        }
    }
}
