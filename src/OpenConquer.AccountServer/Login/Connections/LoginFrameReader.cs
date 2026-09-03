using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login;
using OpenConquer.Protocol.Login.Cryptography;

namespace OpenConquer.AccountServer.Login.Connections;

/// <summary>
/// Reads, decrypts, and validates client-to-server account-login frames from
/// the connection's caller-owned input pipeline.
/// </summary>
internal sealed class LoginFrameReader
{
    private const int ReadStateIdle = 0;
    private const int ReadStateActive = 1;
    private const int ReadStateTerminal = 2;

    private readonly PipeReader _reader;
    private readonly LoginLegacyStreamCipher _cipher;

    private int _readState;

    public LoginFrameReader(PipeReader reader, LoginLegacyStreamCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(cipher);

        _reader = reader;
        _cipher = cipher;
    }

    public async ValueTask<LoginInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();

        byte[] frameBuffer = new byte[LoginProtocolLimits.MaximumFrameLength];

        bool streamStateMayHaveAdvanced = false;
        bool frameOwnershipTransferred = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            int written = 0;
            int frameLength = 0;

            while (true)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                ReadOnlySequence<byte> remaining = result.Buffer;

                SequencePosition consumed = remaining.Start;

                bool frameComplete = false;
                ushort packetId = 0;

                try
                {
                    if (result.IsCanceled)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        throw new OperationCanceledException("The login input pipeline canceled the frame read.");
                    }

                    while (!remaining.IsEmpty)
                    {
                        int targetLength = frameLength == 0 ? WireFrameHeader.Size : frameLength;

                        int bytesNeeded = targetLength - written;

                        int copyLength = (int)Math.Min(remaining.Length, bytesNeeded);

                        ReadOnlySequence<byte> encrypted = remaining.Slice(start: 0, length: copyLength);

                        encrypted.CopyTo(frameBuffer.AsSpan(written, copyLength));

                        remaining = remaining.Slice(copyLength);

                        consumed = remaining.Start;

                        streamStateMayHaveAdvanced = true;

                        _cipher.DecryptInbound(frameBuffer.AsSpan(written, copyLength));

                        written += copyLength;

                        if (frameLength == 0 && written == WireFrameHeader.Size)
                        {
                            _ = WireFrameHeader.TryRead(frameBuffer.AsSpan(0, WireFrameHeader.Size), out WireFrameHeader header);

                            frameLength = header.Length;

                            if (frameLength < WireFrameHeader.Size || frameLength > LoginProtocolLimits.MaximumFrameLength)
                            {
                                throw new InvalidDataException($"Login frame declares invalid length {frameLength} expected {WireFrameHeader.Size} through {LoginProtocolLimits.MaximumFrameLength} bytes.");
                            }
                        }

                        if (frameLength != 0 && written == frameLength)
                        {
                            ReadOnlySequence<byte> plaintextFrame = new(frameBuffer.AsMemory(0, frameLength));

                            WireFrameDecodeStatus status = WireFrameDecoder.Decode(plaintextFrame, LoginProtocolLimits.MaximumFrameLength,
                                out WireFrameHeader header, out _);

                            if (status != WireFrameDecodeStatus.Success)
                            {
                                throw new InvalidDataException($"Login frame validation failed with status '{status}'.");
                            }

                            packetId = header.PacketId;
                            frameComplete = true;

                            break;
                        }
                    }

                    if (!frameComplete && result.IsCompleted)
                    {
                        if (written == 0)
                        {
                            return null;
                        }

                        int expectedLength = frameLength == 0 ? WireFrameHeader.Size : frameLength;

                        throw new EndOfStreamException($"Login input completed with an incomplete frame: {written} of {expectedLength} bytes were received.");
                    }
                }
                finally
                {
                    _reader.AdvanceTo(consumed, consumed);
                }

                if (frameComplete)
                {
                    LoginInboundFrame frame = new(frameBuffer, frameLength, packetId);

                    frameOwnershipTransferred = true;

                    return frame;
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
            if (!frameOwnershipTransferred)
            {
                CryptographicOperations.ZeroMemory(frameBuffer);
            }

            if (Volatile.Read(ref _readState) == ReadStateActive)
            {
                Volatile.Write(ref _readState, ReadStateIdle);
            }
        }
    }

    private void EnterRead()
    {
        int previousState = Interlocked.CompareExchange(ref _readState, ReadStateActive, ReadStateIdle);

        switch (previousState)
        {
            case ReadStateIdle:
                return;

            case ReadStateActive:
                throw new InvalidOperationException("Only one login frame read may be active at a time.");

            case ReadStateTerminal:
                throw new InvalidOperationException("The login frame reader cannot be reused after an input failure.");

            default:
                throw new InvalidOperationException($"Unexpected login frame reader state '{previousState}'.");
        }
    }
}
