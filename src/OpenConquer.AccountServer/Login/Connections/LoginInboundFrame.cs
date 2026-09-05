using System.Security.Cryptography;
using OpenConquer.Protocol.Framing;

namespace OpenConquer.AccountServer.Login.Connections;

/// <summary>
/// Owns one decrypted account login frame.
/// </summary>
internal sealed class LoginInboundFrame : IDisposable
{
    private byte[]? _buffer;

    public LoginInboundFrame(byte[] buffer, int frameLength, ushort packetId)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameLength, WireFrameHeader.Size);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameLength, buffer.Length);

        if (packetId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetId), "Packet identifier 0 is invalid.");
        }

        _buffer = buffer;
        FrameLength = frameLength;
        PacketId = packetId;
    }

    public int FrameLength { get; }
    public ushort PacketId { get; }

    public ReadOnlyMemory<byte> Payload
    {
        get
        {
            byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(LoginInboundFrame));
            return buffer.AsMemory(WireFrameHeader.Size, FrameLength - WireFrameHeader.Size);
        }
    }

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);

        if (buffer is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(buffer);
    }
}
