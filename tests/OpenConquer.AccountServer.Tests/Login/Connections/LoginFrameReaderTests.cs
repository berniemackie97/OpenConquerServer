using System.Buffers;
using System.IO.Pipelines;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Login.Cryptography;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginFrameReaderTests
{
    private const string FirstEncryptedFrame = "D48487651720B0C3";

    private const string SecondEncryptedFrame = "5F0F01E593AE364E";

    [Fact]
    public void Constructor_RejectsNullReader()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LoginFrameReader(null!, new LoginLegacyStreamCipher())
        );
    }

    [Fact]
    public async Task Constructor_RejectsNullCipher()
    {
        Pipe pipe = new();

        try
        {
            Assert.Throws<ArgumentNullException>(() => new LoginFrameReader(pipe.Reader, null!));
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_DecryptsVerifiedNativeFrame()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            await pipe.Writer.WriteAsync(
                Convert.FromHexString(FirstEncryptedFrame),
                TestContext.Current.CancellationToken
            );

            using LoginInboundFrame frame = Assert.IsType<LoginInboundFrame>(
                await reader.ReadAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(LoginSeedPacket.PacketIdentifier, frame.PacketId);

            Assert.Equal(8, frame.FrameLength);

            Assert.Equal(Convert.FromHexString("78563412"), frame.Payload.ToArray());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_PreservesInboundCipherStateAcrossFrames()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            byte[] ciphertext = Convert.FromHexString(FirstEncryptedFrame + SecondEncryptedFrame);

            await pipe.Writer.WriteAsync(ciphertext, TestContext.Current.CancellationToken);

            using LoginInboundFrame first = Assert.IsType<LoginInboundFrame>(
                await reader.ReadAsync(TestContext.Current.CancellationToken)
            );

            using LoginInboundFrame second = Assert.IsType<LoginInboundFrame>(
                await reader.ReadAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(LoginSeedPacket.PacketIdentifier, first.PacketId);

            Assert.Equal(LoginSeedPacket.PacketIdentifier, second.PacketId);

            Assert.Equal(Convert.FromHexString("78563412"), first.Payload.ToArray());

            Assert.Equal(Convert.FromHexString("78563412"), second.Payload.ToArray());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_PreservesPartialFrameAcrossPipeReads()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            await pipe.Writer.WriteAsync(
                Convert.FromHexString("D484"),
                TestContext.Current.CancellationToken
            );

            Task<LoginInboundFrame?> pendingRead = reader
                .ReadAsync(TestContext.Current.CancellationToken)
                .AsTask();

            Assert.False(pendingRead.IsCompleted);

            await pipe.Writer.WriteAsync(
                Convert.FromHexString("87651720B0C3"),
                TestContext.Current.CancellationToken
            );

            using LoginInboundFrame frame = Assert.IsType<LoginInboundFrame>(await pendingRead);

            Assert.Equal(LoginSeedPacket.PacketIdentifier, frame.PacketId);

            Assert.Equal(Convert.FromHexString("78563412"), frame.Payload.ToArray());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_CleanCompletionBeforeFrameReturnsNull()
    {
        Pipe pipe = new();

        await pipe.Writer.CompleteAsync();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            LoginInboundFrame? frame = await reader.ReadAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Null(frame);
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_PartialFrameAtCompletionFailsAndPoisonsReader()
    {
        Pipe pipe = new();

        await pipe.Writer.WriteAsync(
            Convert.FromHexString("D484"),
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.CompleteAsync();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            EndOfStreamException exception = await Assert.ThrowsAsync<EndOfStreamException>(() =>
                reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            );

            Assert.Contains("2 of 4 bytes", exception.Message);

            InvalidOperationException terminalException =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                );

            Assert.Equal(
                "The login frame reader cannot be reused after an input failure.",
                terminalException.Message
            );
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidFrameLengthFailsAndPoisonsReader()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            await pipe.Writer.WriteAsync(
                Convert.FromHexString("6484A525"),
                TestContext.Current.CancellationToken
            );

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            );

            Assert.Contains("invalid length 3", exception.Message);

            InvalidOperationException terminalException =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                );

            Assert.Equal(
                "The login frame reader cannot be reused after an input failure.",
                terminalException.Message
            );
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_PreCanceledOperationDoesNotAdvanceCipher()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            using CancellationTokenSource cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    TestContext.Current.CancellationToken
                );

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                reader.ReadAsync(cancellation.Token).AsTask()
            );

            await pipe.Writer.WriteAsync(
                Convert.FromHexString(FirstEncryptedFrame),
                TestContext.Current.CancellationToken
            );

            using LoginInboundFrame frame = Assert.IsType<LoginInboundFrame>(
                await reader.ReadAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(Convert.FromHexString("78563412"), frame.Payload.ToArray());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_RejectsConcurrentReadWithoutPoisoningUnusedCipher()
    {
        Pipe pipe = new();

        try
        {
            LoginFrameReader reader = new(pipe.Reader, new LoginLegacyStreamCipher());

            Task<LoginInboundFrame?> firstRead = reader
                .ReadAsync(TestContext.Current.CancellationToken)
                .AsTask();

            Assert.False(firstRead.IsCompleted);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                );

            Assert.Equal("Only one login frame read may be active at a time.", exception.Message);

            pipe.Reader.CancelPendingRead();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);

            await pipe.Writer.WriteAsync(
                Convert.FromHexString(FirstEncryptedFrame),
                TestContext.Current.CancellationToken
            );

            using LoginInboundFrame frame = Assert.IsType<LoginInboundFrame>(
                await reader.ReadAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(Convert.FromHexString("78563412"), frame.Payload.ToArray());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_AdvanceFailurePoisonsReader()
    {
        Pipe pipe = new();

        AdvanceThrowingPipeReader throwingReader = new(pipe.Reader);

        try
        {
            LoginFrameReader reader = new(throwingReader, new LoginLegacyStreamCipher());

            await pipe.Writer.WriteAsync(
                Convert.FromHexString(FirstEncryptedFrame),
                TestContext.Current.CancellationToken
            );

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                );

            Assert.Equal("Synthetic AdvanceTo failure.", exception.Message);

            InvalidOperationException terminalException =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                );

            Assert.Equal(
                "The login frame reader cannot be reused after an input failure.",
                terminalException.Message
            );
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await throwingReader.CompleteAsync();
        }
    }

    private sealed class AdvanceThrowingPipeReader(PipeReader inner) : PipeReader
    {
        public override bool TryRead(out ReadResult result)
        {
            return inner.TryRead(out result);
        }

        public override ValueTask<ReadResult> ReadAsync(
            CancellationToken cancellationToken = default
        )
        {
            return inner.ReadAsync(cancellationToken);
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            throw new InvalidOperationException("Synthetic AdvanceTo failure.");
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            throw new InvalidOperationException("Synthetic AdvanceTo failure.");
        }

        public override void CancelPendingRead()
        {
            inner.CancelPendingRead();
        }

        public override void Complete(Exception? exception = null)
        {
            inner.Complete(exception);
        }
    }
}
