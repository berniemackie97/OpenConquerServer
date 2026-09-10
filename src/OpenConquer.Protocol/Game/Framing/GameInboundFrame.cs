using System.Buffers;
using OpenConquer.Protocol.Framing;

namespace OpenConquer.Protocol.Game.Framing;

/// <summary>
/// Owns one decrypted 5517 GameServer packet buffer.
/// </summary>
public sealed class GameInboundFrame : IDisposable
{
    private byte[]? _buffer;

    internal GameInboundFrame(byte[] buffer, WireFrameHeader header)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Header = header;
    }

    public WireFrameHeader Header { get; }

    public ushort PacketId => Header.PacketId;

    public ReadOnlyMemory<byte> Packet => (_buffer ?? throw new ObjectDisposedException(nameof(GameInboundFrame))).AsMemory(0, Header.Length);

    public ReadOnlyMemory<byte> Payload => Packet[WireFrameHeader.Size..];

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);

        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
