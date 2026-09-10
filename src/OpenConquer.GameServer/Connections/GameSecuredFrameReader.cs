using System.Buffers;
using System.IO.Pipelines;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.GameServer.Connections;

/// <summary>
/// Reads secured client-to-server GameServer frames from a caller-owned input pipeline.
/// </summary>
internal sealed class GameSecuredFrameReader : IDisposable
{
    private const int ReadStateIdle = 0;
    private const int ReadStateActive = 1;
    private const int ReadStateTerminal = 2;

    private readonly PipeReader _reader;
    private readonly GameSecuredFrameDecoder _decoder;

    private int _readState;
    private int _disposeState;

    public GameSecuredFrameReader(PipeReader reader, GameSessionCipher sessionCipher)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sessionCipher);

        _reader = reader;
        _decoder = new GameSecuredFrameDecoder(sessionCipher);
    }

    public async ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();

        bool streamStateMayHaveAdvanced = false;
        bool frameStarted = false;

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
                        throw new OperationCanceledException("The game input pipeline canceled the secured frame read.");
                    }

                    long bytesConsumed = 0;

                    foreach (ReadOnlyMemory<byte> segment in buffer)
                    {
                        if (segment.IsEmpty)
                        {
                            continue;
                        }

                        GameSecuredFrameDecodeResult decodeResult = _decoder.Decode(segment.Span);

                        if ((uint)decodeResult.BytesConsumed > (uint)segment.Length)
                        {
                            throw new InvalidOperationException($"The secured frame decoder consumed {decodeResult.BytesConsumed} bytes from a {segment.Length}-byte input segment.");
                        }

                        if (decodeResult.BytesConsumed > 0)
                        {
                            bytesConsumed += decodeResult.BytesConsumed;
                            streamStateMayHaveAdvanced = true;
                            frameStarted = true;
                        }

                        switch (decodeResult.Status)
                        {
                            case GameSecuredFrameDecodeStatus.NeedMoreData:
                                if (decodeResult.BytesConsumed != segment.Length)
                                {
                                    throw new InvalidOperationException("The secured frame decoder returned NeedMoreData without consuming the complete input segment.");
                                }

                                break;

                            case GameSecuredFrameDecodeStatus.Success:
                                consumed = buffer.GetPosition(bytesConsumed);
                                examined = consumed;

                                return decodeResult.Frame ?? throw new InvalidOperationException("The secured frame decoder reported success without returning a frame.");

                            case GameSecuredFrameDecodeStatus.InvalidPacketLength:
                            case GameSecuredFrameDecodeStatus.InvalidSignature:
                                consumed = buffer.GetPosition(bytesConsumed);
                                examined = consumed;

                                throw new InvalidDataException($"GameServer secured frame validation failed with status '{decodeResult.Status}'.");

                            default:
                                throw new InvalidOperationException($"Unexpected secured frame decode status '{decodeResult.Status}'.");
                        }
                    }

                    consumed = buffer.End;
                    examined = buffer.End;

                    if (result.IsCompleted)
                    {
                        if (!frameStarted)
                        {
                            return null;
                        }

                        throw new EndOfStreamException("GameServer input completed with an incomplete secured frame.");
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
            if (streamStateMayHaveAdvanced)
            {
                Volatile.Write(ref _readState, ReadStateTerminal);
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _readState, ReadStateTerminal);
        _decoder.Dispose();
    }

    private void EnterRead()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        int previousState = Interlocked.CompareExchange(ref _readState, ReadStateActive, ReadStateIdle);

        switch (previousState)
        {
            case ReadStateIdle:
                return;

            case ReadStateActive:
                throw new InvalidOperationException("Only one secured game frame read may be active at a time.");

            case ReadStateTerminal:
                throw new InvalidOperationException("The secured game frame reader cannot be reused after an input failure.");

            default:
                throw new InvalidOperationException($"Unexpected secured game frame reader state '{previousState}'.");
        }
    }
}
