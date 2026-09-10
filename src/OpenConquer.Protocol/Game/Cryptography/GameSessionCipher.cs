namespace OpenConquer.Protocol.Game.Cryptography;

/// <summary>
/// Owns the independent inbound and outbound CAST5 CFB-64 streams negotiated
/// for one 5517 GameServer connection.
/// </summary>
public sealed class GameSessionCipher : IDisposable
{
    private readonly object _inboundGate = new();
    private readonly object _outboundGate = new();
    private readonly GameCast5Cfb64Cipher _inbound;
    private readonly GameCast5Cfb64Cipher _outbound;

    private int _disposed;

    internal GameSessionCipher(ReadOnlySpan<byte> key, ReadOnlySpan<byte> inboundInitializationVector, ReadOnlySpan<byte> outboundInitializationVector)
    {
        _inbound = new GameCast5Cfb64Cipher(key, inboundInitializationVector, encrypt: false);

        try
        {
            _outbound = new GameCast5Cfb64Cipher(key, outboundInitializationVector, encrypt: true);
        }
        catch
        {
            _inbound.Dispose();
            throw;
        }
    }

    internal void DecryptInbound(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
    {
        lock (_inboundGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _inbound.Transform(ciphertext, plaintext);
        }
    }

    internal void EncryptOutbound(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        lock (_outboundGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _outbound.Transform(plaintext, ciphertext);
        }
    }

    public void Dispose()
    {
        lock (_inboundGate)
        {
            lock (_outboundGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _inbound.Dispose();
                _outbound.Dispose();
            }
        }
    }
}
