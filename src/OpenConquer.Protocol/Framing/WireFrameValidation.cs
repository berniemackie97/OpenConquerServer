namespace OpenConquer.Protocol.Framing;

internal static class WireFrameValidation
{
    public static bool IsPacketIdValid(ushort packetId)
    {
        return packetId != 0;
    }

    public static bool IsFrameLengthValid(int frameLength, int maximumFrameLength)
    {
        return frameLength >= WireFrameHeader.Size && frameLength <= maximumFrameLength;
    }

    public static void ValidateMaximumFrameLength(int maximumFrameLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFrameLength, other: WireFrameHeader.Size);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFrameLength, other: ushort.MaxValue);
    }
}
