using System.IO.Pipelines;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;

namespace OpenConquer.GameServer.Connections;

/// <summary>
/// Serializes secured server-to-client GameServer frames into a caller-owned output pipeline.
/// </summary>
internal sealed class GameSecuredFrameWriter
{
    private readonly PipeWriter _writer;
    private readonly GameSessionCipher _sessionCipher;
    private readonly SemaphoreSlim _writeGate = new(initialCount: 1, maxCount: 1);

    private int _terminalState;

    public GameSecuredFrameWriter(PipeWriter writer, GameSessionCipher sessionCipher)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sessionCipher);

        _writer = writer;
        _sessionCipher = sessionCipher;
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        cancellationToken.ThrowIfCancellationRequested();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        bool streamStateMayHaveAdvanced = false;

        try
        {
            if (Volatile.Read(ref _terminalState) != 0)
            {
                throw new InvalidOperationException("The secured game frame writer cannot be reused after an output failure.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            int packetLength = GameWireFrameEncoder.GetFrameLength(packet);
            int wireLength = checked(packetLength + GameWireProtocol.SignatureLength);

            Memory<byte> destination = _writer.GetMemory(wireLength);

            int writtenPacketLength = GameWireFrameEncoder.WriteFrame(packet, destination.Span[..packetLength]);

            if (writtenPacketLength != packetLength)
            {
                throw new InvalidOperationException($"GameServer packet encoding produced {writtenPacketLength} bytes after declaring a frame length of {packetLength} bytes.");
            }

            streamStateMayHaveAdvanced = true;

            int writtenWireLength = GameSecuredFrameEncoder.WriteFrame(_sessionCipher, destination.Span[..packetLength], destination.Span[..wireLength]);

            if (writtenWireLength != wireLength)
            {
                throw new InvalidOperationException($"GameServer secured frame encoding produced {writtenWireLength} bytes after declaring a wire length of {wireLength} bytes.");
            }

            _writer.Advance(wireLength);

            FlushResult flush = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (flush.IsCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException("The game output pipeline canceled the secured frame flush.");
            }

            if (flush.IsCompleted)
            {
                throw new InvalidOperationException("The game output pipeline is completed.");
            }
        }
        catch
        {
            if (streamStateMayHaveAdvanced)
            {
                Volatile.Write(ref _terminalState, 1);
            }

            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
