using System.Buffers;
using System.IO.Pipelines;
using OpenConquer.Protocol.Game.Handshake;

namespace OpenConquer.GameServer.Connections;

/// <summary>
/// Reads one bootstrap-encrypted client Diffie-Hellman response from a caller-owned input pipeline.
/// </summary>
internal sealed class GameClientKeyExchangeResponseReader
{
    private const int ReadStateIdle = 0;
    private const int ReadStateActive = 1;
    private const int ReadStateCompleted = 2;
    private const int ReadStateTerminal = 3;

    private readonly PipeReader _reader;

    private int _readState;

    public GameClientKeyExchangeResponseReader(PipeReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.End;

                try
                {
                    if (result.IsCanceled)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new OperationCanceledException("The game input pipeline canceled the client key-exchange response read.");
                    }

                    if (!buffer.IsEmpty)
                    {
                        GameClientKeyExchangeDecodeResult decodeResult = Decode(buffer);

                        switch (decodeResult.Status)
                        {
                            case GameClientKeyExchangeDecodeStatus.Success:
                                if (decodeResult.BytesConsumed <= 0 || decodeResult.BytesConsumed > buffer.Length)
                                {
                                    throw new InvalidOperationException($"The client key-exchange decoder reported an invalid consumed length of {decodeResult.BytesConsumed} bytes.");
                                }

                                consumed = buffer.GetPosition(decodeResult.BytesConsumed);
                                examined = consumed;

                                Volatile.Write(ref _readState, ReadStateCompleted);

                                return decodeResult.ClientPublicKeyHex
                                    ?? throw new InvalidOperationException("The client key-exchange decoder reported success without returning a public key.");

                            case GameClientKeyExchangeDecodeStatus.NeedMoreData:
                                if (decodeResult.RequiredBytes <= 0)
                                {
                                    throw new InvalidOperationException($"The client key-exchange decoder reported an invalid required length of {decodeResult.RequiredBytes} bytes.");
                                }

                                if (buffer.Length >= decodeResult.RequiredBytes)
                                {
                                    throw new InvalidOperationException($"The client key-exchange decoder requested {decodeResult.RequiredBytes} bytes despite receiving {buffer.Length} buffered bytes.");
                                }

                                break;

                            case GameClientKeyExchangeDecodeStatus.InvalidDeclaredLength:
                            case GameClientKeyExchangeDecodeStatus.InvalidSignature:
                            case GameClientKeyExchangeDecodeStatus.InvalidPayload:
                                Volatile.Write(ref _readState, ReadStateTerminal);
                                throw new InvalidDataException($"GameServer client key-exchange response validation failed with status '{decodeResult.Status}'.");

                            default:
                                Volatile.Write(ref _readState, ReadStateTerminal);
                                throw new InvalidOperationException($"Unexpected client key-exchange decode status '{decodeResult.Status}'.");
                        }
                    }

                    if (result.IsCompleted)
                    {
                        Volatile.Write(ref _readState, ReadStateCompleted);

                        if (buffer.IsEmpty)
                        {
                            return null;
                        }

                        Volatile.Write(ref _readState, ReadStateTerminal);
                        throw new EndOfStreamException("GameServer input completed with an incomplete client key-exchange response.");
                    }
                }
                finally
                {
                    _reader.AdvanceTo(consumed, examined);
                }
            }
        }
        catch
        {
            if (Volatile.Read(ref _readState) == ReadStateActive)
            {
                Volatile.Write(ref _readState, ReadStateIdle);
            }

            throw;
        }
        finally
        {
            if (Volatile.Read(ref _readState) == ReadStateActive)
            {
                Volatile.Write(ref _readState, ReadStateIdle);
            }
        }
    }

    private static GameClientKeyExchangeDecodeResult Decode(ReadOnlySequence<byte> buffer)
    {
        ReadOnlySpan<byte> firstSpan = buffer.FirstSpan;
        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(firstSpan);

        while (result.Status == GameClientKeyExchangeDecodeStatus.NeedMoreData && buffer.Length >= result.RequiredBytes)
        {
            int requiredBytes = result.RequiredBytes;

            if (requiredBytes <= 0)
            {
                throw new InvalidOperationException($"The client key-exchange decoder reported an invalid required length of {requiredBytes} bytes.");
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(requiredBytes);

            try
            {
                buffer.Slice(0, requiredBytes).CopyTo(rented);

                result = GameClientKeyExchangeResponseDecoder.Decode(rented.AsSpan(0, requiredBytes));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }

        return result;
    }

    private void EnterRead()
    {
        int previousState = Interlocked.CompareExchange(ref _readState, ReadStateActive, ReadStateIdle);

        switch (previousState)
        {
            case ReadStateIdle:
                return;

            case ReadStateActive:
                throw new InvalidOperationException("Only one client key-exchange response read may be active at a time.");

            case ReadStateCompleted:
                throw new InvalidOperationException("The client key-exchange response has already been read.");

            case ReadStateTerminal:
                throw new InvalidOperationException("The client key-exchange response reader cannot be reused after a protocol failure.");

            default:
                throw new InvalidOperationException($"Unexpected client key-exchange response reader state '{previousState}'.");
        }
    }
}
