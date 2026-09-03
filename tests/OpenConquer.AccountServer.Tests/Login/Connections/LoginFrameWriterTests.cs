using System.Buffers;
using System.IO.Pipelines;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login;
using OpenConquer.Protocol.Login.Cryptography;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginFrameWriterTests
{
    [Fact]
    public void Constructor_RejectsNullWriter()
    {
        LoginLegacyStreamCipher cipher = new();

        Assert.Throws<ArgumentNullException>(() => new LoginFrameWriter(null!, cipher));
    }

    [Fact]
    public async Task Constructor_RejectsNullCipher()
    {
        Pipe pipe = new();

        try
        {
            Assert.Throws<ArgumentNullException>(() => new LoginFrameWriter(pipe.Writer, null!));
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_RejectsNullPacket()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                writer.WriteAsync(null!, TestContext.Current.CancellationToken).AsTask()
            );
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_WritesVerifiedEncryptedLoginSeedFrame()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            await writer.WriteAsync(
                new LoginSeedPacket(seed: 0x1234_5678),
                TestContext.Current.CancellationToken
            );

            byte[] actual = await ReadAvailableAsync(
                pipe.Reader,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(Convert.FromHexString("C54869128E317C0F"), actual);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_PreservesOutboundCipherStateAcrossFrames()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            LoginSeedPacket packet = new(seed: 0x1234_5678);

            await writer.WriteAsync(packet, TestContext.Current.CancellationToken);

            await writer.WriteAsync(packet, TestContext.Current.CancellationToken);

            byte[] actual = await ReadAvailableAsync(
                pipe.Reader,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(Convert.FromHexString("C54869128E317C0F7DF0011AC6D914D7"), actual);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_RejectsOversizedFrameWithoutAdvancingCipherOrWritingBytes()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                writer
                    .WriteAsync(new OversizedLoginPacket(), TestContext.Current.CancellationToken)
                    .AsTask()
            );

            await writer.WriteAsync(
                new LoginSeedPacket(seed: 0x1234_5678),
                TestContext.Current.CancellationToken
            );

            byte[] actual = await ReadAvailableAsync(
                pipe.Reader,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(Convert.FromHexString("C54869128E317C0F"), actual);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_PreCanceledOperationDoesNotAdvanceCipherOrWriteBytes()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            using CancellationTokenSource cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    TestContext.Current.CancellationToken
                );

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                writer
                    .WriteAsync(new LoginSeedPacket(seed: 0x1234_5678), cancellation.Token)
                    .AsTask()
            );

            await writer.WriteAsync(
                new LoginSeedPacket(seed: 0x1234_5678),
                TestContext.Current.CancellationToken
            );

            byte[] actual = await ReadAvailableAsync(
                pipe.Reader,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(Convert.FromHexString("C54869128E317C0F"), actual);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_RejectsConcurrentWrite()
    {
        Pipe pipe = new(
            new PipeOptions(
                pauseWriterThreshold: 1,
                resumeWriterThreshold: 1,
                useSynchronizationContext: false
            )
        );

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            Task firstWrite = writer
                .WriteAsync(
                    new LoginSeedPacket(seed: 0x1234_5678),
                    TestContext.Current.CancellationToken
                )
                .AsTask();

            Assert.False(firstWrite.IsCompleted);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    writer
                        .WriteAsync(
                            new LoginSeedPacket(seed: 0x1234_5678),
                            TestContext.Current.CancellationToken
                        )
                        .AsTask()
                );

            Assert.Equal("Only one login frame write may be active at a time.", exception.Message);

            ReadResult result = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

            pipe.Reader.AdvanceTo(result.Buffer.End);

            await firstWrite;
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_FailedFlushPermanentlyPoisonsWriter()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameWriter writer = new(pipe.Writer, new LoginLegacyStreamCipher());

            pipe.Writer.CancelPendingFlush();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                writer
                    .WriteAsync(
                        new LoginSeedPacket(seed: 0x1234_5678),
                        TestContext.Current.CancellationToken
                    )
                    .AsTask()
            );

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    writer
                        .WriteAsync(
                            new LoginSeedPacket(seed: 0x1234_5678),
                            TestContext.Current.CancellationToken
                        )
                        .AsTask()
                );

            Assert.Equal(
                "The login frame writer cannot be reused after an output failure.",
                exception.Message
            );
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    private static async Task<byte[]> ReadAvailableAsync(
        PipeReader reader,
        CancellationToken cancellationToken
    )
    {
        ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        ReadOnlySequence<byte> buffer = result.Buffer;

        byte[] bytes = buffer.ToArray();

        reader.AdvanceTo(buffer.End);

        return bytes;
    }

    private sealed class OversizedLoginPacket : IPacket
    {
        public ushort PacketId => 1;

        public int PayloadLength =>
            LoginProtocolLimits.MaximumFrameLength - WireFrameHeader.Size + 1;

        public void WritePayload(ref PacketWriter writer)
        {
            throw new InvalidOperationException(
                "An oversized login packet must be rejected before payload serialization."
            );
        }
    }
}
