namespace OpenConquer.Protocol.Game;

public static class GameWireProtocol
{
    public const int MaximumPacketLength = 0x400;
    public const int SignatureLength = 8;
    public const int MaximumWireFrameLength = MaximumPacketLength + SignatureLength;

    public static ReadOnlySpan<byte> ClientSignature => "TQClient"u8;
    public static ReadOnlySpan<byte> ServerSignature => "TQServer"u8;
}
