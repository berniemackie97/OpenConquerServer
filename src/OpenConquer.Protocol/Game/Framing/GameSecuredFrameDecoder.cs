using System.Buffers;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Game.Framing;

public enum GameSecuredFrameDecodeStatus
{
    Success = 0,
    NeedMoreData,
    InvalidPacketLength,
    InvalidSignature,
}

public readonly record struct GameSecuredFrameDecodeResult(GameSecuredFrameDecodeStatus Status, int BytesConsumed, GameInboundFrame? Frame)
{
    public bool Succeeded => Status == GameSecuredFrameDecodeStatus.Success;
}

/// <summary>
/// Incrementally decrypts one 5517 client GameServer frame while preserving
/// directional CAST5 CFB-64 state across arbitrary TCP fragmentation.
/// </summary>
public sealed class GameSecuredFrameDecoder : IDisposable
{
    private readonly object _gate = new();
    private readonly GameSessionCipher _sessionCipher;

    private byte[]? _buffer;
    private WireFrameHeader _header;
    private int _bufferedLength;
    private int _expectedWireLength;
    private bool _faulted;
    private bool _disposed;

    public GameSecuredFrameDecoder(GameSessionCipher sessionCipher)
    {
        _sessionCipher = sessionCipher ?? throw new ArgumentNullException(nameof(sessionCipher));
    }

    public GameSecuredFrameDecodeResult Decode(ReadOnlySpan<byte> encryptedSource)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_faulted)
            {
                throw new InvalidOperationException("The secured frame decoder cannot continue after a framing failure.");
            }

            if (encryptedSource.IsEmpty)
            {
                return NeedMoreData(bytesConsumed: 0);
            }

            byte[] buffer = _buffer ??= ArrayPool<byte>.Shared.Rent(GameWireProtocol.MaximumWireFrameLength);
            int bytesConsumed = 0;

            try
            {
                if (_bufferedLength < WireFrameHeader.Size)
                {
                    int headerBytes = Math.Min(WireFrameHeader.Size - _bufferedLength, encryptedSource.Length);

                    _sessionCipher.DecryptInbound(
                        encryptedSource[..headerBytes],
                        buffer.AsSpan(_bufferedLength, headerBytes));

                    _bufferedLength += headerBytes;
                    bytesConsumed += headerBytes;

                    if (_bufferedLength < WireFrameHeader.Size)
                    {
                        return NeedMoreData(bytesConsumed);
                    }

                    if (!WireFrameHeader.TryRead(buffer.AsSpan(0, WireFrameHeader.Size), out WireFrameHeader header) ||
                        header.Length is < WireFrameHeader.Size or > GameWireProtocol.MaximumPacketLength)
                    {
                        return Fail(GameSecuredFrameDecodeStatus.InvalidPacketLength, bytesConsumed);
                    }

                    _header = header;
                    _expectedWireLength = header.Length + GameWireProtocol.SignatureLength;
                }

                int remainingLength = _expectedWireLength - _bufferedLength;
                int availableLength = encryptedSource.Length - bytesConsumed;
                int bytesToDecrypt = Math.Min(remainingLength, availableLength);

                if (bytesToDecrypt > 0)
                {
                    _sessionCipher.DecryptInbound(
                        encryptedSource.Slice(bytesConsumed, bytesToDecrypt),
                        buffer.AsSpan(_bufferedLength, bytesToDecrypt));

                    _bufferedLength += bytesToDecrypt;
                    bytesConsumed += bytesToDecrypt;
                }

                if (_bufferedLength < _expectedWireLength)
                {
                    return NeedMoreData(bytesConsumed);
                }

                if (!buffer.AsSpan(_header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ClientSignature))
                {
                    return Fail(GameSecuredFrameDecodeStatus.InvalidSignature, bytesConsumed);
                }

                GameInboundFrame frame = new(buffer, _header);

                _buffer = null;
                ResetFrameState();

                return new GameSecuredFrameDecodeResult(GameSecuredFrameDecodeStatus.Success, bytesConsumed, frame);
            }
            catch
            {
                ReleaseBuffer();
                _faulted = true;
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseBuffer();
        }
    }

    private GameSecuredFrameDecodeResult Fail(GameSecuredFrameDecodeStatus status, int bytesConsumed)
    {
        _faulted = true;
        ReleaseBuffer();
        return new GameSecuredFrameDecodeResult(status, bytesConsumed, null);
    }

    private static GameSecuredFrameDecodeResult NeedMoreData(int bytesConsumed) =>
        new(GameSecuredFrameDecodeStatus.NeedMoreData, bytesConsumed, null);

    private void ReleaseBuffer()
    {
        byte[]? buffer = _buffer;
        _buffer = null;

        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        ResetFrameState();
    }

    private void ResetFrameState()
    {
        _header = default;
        _bufferedLength = 0;
        _expectedWireLength = 0;
    }
}
