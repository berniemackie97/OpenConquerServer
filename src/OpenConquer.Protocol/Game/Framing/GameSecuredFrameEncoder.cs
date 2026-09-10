using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Game.Framing;

/// <summary>
/// Appends the native server trailer and encrypts complete 5517 GameServer frames.
/// </summary>
public static class GameSecuredFrameEncoder
{
    public static int GetWireLength(ReadOnlySpan<byte> packet)
    {
        int packetLength = ValidatePacket(packet);
        return packetLength + GameWireProtocol.SignatureLength;
    }

    public static int WriteFrame(GameSessionCipher sessionCipher, ReadOnlySpan<byte> packet, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(sessionCipher);

        int packetLength = ValidatePacket(packet);
        int wireLength = packetLength + GameWireProtocol.SignatureLength;

        if (destination.Length < wireLength)
        {
            throw new ArgumentException($"Destination must contain at least {wireLength} bytes.", nameof(destination));
        }

        Span<byte> wireFrame = destination[..wireLength];

        packet.CopyTo(wireFrame);
        GameWireProtocol.ServerSignature.CopyTo(wireFrame[packetLength..]);

        try
        {
            sessionCipher.EncryptOutbound(wireFrame, wireFrame);
            return wireLength;
        }
        catch
        {
            wireFrame.Clear();
            throw;
        }
    }

    private static int ValidatePacket(ReadOnlySpan<byte> packet)
    {
        if (!WireFrameHeader.TryRead(packet, out WireFrameHeader header) ||
            header.Length != packet.Length ||
            header.Length is < WireFrameHeader.Size or > GameWireProtocol.MaximumPacketLength)
        {
            throw new ArgumentException("Packet does not contain a valid 5517 GameServer wire-frame header.", nameof(packet));
        }

        return header.Length;
    }
}
