using System.Buffers;

namespace OpenConquer.Protocol.Framing;

/// <summary>
/// Extracts and validates complete TQ wire frames from caller owned buffered input.
/// </summary>
public static class WireFrameDecoder
{
    public static WireFrameDecodeStatus Decode(ReadOnlySequence<byte> source, out WireFrameHeader header, out ReadOnlySequence<byte> frame)
    {
        return Decode(source, maximumFrameLength: ushort.MaxValue, out header, out frame);
    }

    public static WireFrameDecodeStatus Decode(ReadOnlySequence<byte> source, int maximumFrameLength, out WireFrameHeader header, out ReadOnlySequence<byte> frame)
    {
        WireFrameValidation.ValidateMaximumFrameLength(maximumFrameLength);

        frame = ReadOnlySequence<byte>.Empty;

        if (source.Length < WireFrameHeader.Size)
        {
            header = default;
            return WireFrameDecodeStatus.IncompleteHeader;
        }

        header = ReadHeader(source);

        if (!WireFrameValidation.IsFrameLengthValid(header.Length, maximumFrameLength))
        {
            return WireFrameDecodeStatus.InvalidFrameLength;
        }

        if (source.Length < header.Length)
        {
            return WireFrameDecodeStatus.IncompleteFrame;
        }

        if (!WireFrameValidation.IsPacketIdValid(header.PacketId))
        {
            return WireFrameDecodeStatus.InvalidPacketId;
        }

        frame = source.Slice(start: 0, header.Length);

        return WireFrameDecodeStatus.Success;
    }

    private static WireFrameHeader ReadHeader(ReadOnlySequence<byte> source)
    {
        ReadOnlySpan<byte> firstSpan = source.FirstSpan;

        if (firstSpan.Length >= WireFrameHeader.Size)
        {
            _ = WireFrameHeader.TryRead(firstSpan, out WireFrameHeader header);

            return header;
        }

        Span<byte> headerBytes = stackalloc byte[WireFrameHeader.Size];

        source.Slice(start: 0, length: WireFrameHeader.Size).CopyTo(headerBytes);

        _ = WireFrameHeader.TryRead(headerBytes, out WireFrameHeader segmentedHeader);

        return segmentedHeader;
    }
}
