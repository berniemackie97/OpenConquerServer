using System.IO.Pipelines;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login;
using OpenConquer.Protocol.Login.Cryptography;
using OpenConquer.Protocol.Packets;

namespace OpenConquer.AccountServer.Login.Connections;

/// <summary>
/// Encodes and encrypts server-to-client account-login frames into the
/// connection's caller-owned output pipeline.
/// </summary>
internal sealed class LoginFrameWriter
{
    private const int WriteStateIdle = 0;
    private const int WriteStateActive = 1;
    private const int WriteStateTerminal = 2;

    private readonly PipeWriter _writer;
    private readonly LoginLegacyStreamCipher _cipher;

    private int _writeState;

    public LoginFrameWriter(PipeWriter writer, LoginLegacyStreamCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(cipher);

        _writer = writer;
        _cipher = cipher;
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        EnterWrite();

        bool streamStateMayHaveAdvanced = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            int frameLength = WireFrameEncoder.GetFrameLength(packet, LoginProtocolLimits.MaximumFrameLength);

            Memory<byte> destination = _writer.GetMemory(frameLength);

            Span<byte> frame = destination.Span[..frameLength];

            int written = WireFrameEncoder.WriteFrame(packet, frame, LoginProtocolLimits.MaximumFrameLength);

            streamStateMayHaveAdvanced = true;

            _cipher.EncryptOutbound(frame[..written]);

            _writer.Advance(written);

            FlushResult flush = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (flush.IsCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();

                throw new OperationCanceledException("The login output pipeline canceled the frame flush.");
            }

            if (flush.IsCompleted)
            {
                throw new InvalidOperationException("The login output pipeline is completed.");
            }
        }
        catch
        {
            if (streamStateMayHaveAdvanced)
            {
                Volatile.Write(ref _writeState, WriteStateTerminal);
            }

            throw;
        }
        finally
        {
            if (Volatile.Read(ref _writeState) == WriteStateActive)
            {
                Volatile.Write(ref _writeState, WriteStateIdle);
            }
        }
    }

    private void EnterWrite()
    {
        int previousState = Interlocked.CompareExchange(ref _writeState, WriteStateActive, WriteStateIdle);

        switch (previousState)
        {
            case WriteStateIdle:
                return;

            case WriteStateActive:
                throw new InvalidOperationException("Only one login frame write may be active at a time.");

            case WriteStateTerminal:
                throw new InvalidOperationException("The login frame writer cannot be reused after an output failure.");

            default:
                throw new InvalidOperationException($"Unexpected login frame writer state '{previousState}'.");
        }
    }
}
