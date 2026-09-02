namespace OpenConquer.Protocol.Framing;

/// <summary>
/// Describes the result of attempting to decode the first complete TQ wire frame from buffered input.
/// </summary>
public enum WireFrameDecodeStatus
{
    Success = 0,
    IncompleteHeader,
    IncompleteFrame,
    InvalidFrameLength,
    InvalidPacketId,
}
