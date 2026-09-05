using System.Buffers;
using System.Net;
using System.Threading.Channels;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginConnectionSessionTests
{
    private const uint VerifiedSeed = 0x1234_5678;

    [Fact]
    public async Task OpenAsync_RejectsNullConnection()
    {
        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            LoginConnectionSession
                .OpenAsync(null!, seedGenerator, TestContext.Current.CancellationToken)
                .AsTask()
        );
    }

    [Fact]
    public async Task OpenAsync_RejectsNullSeedGenerator()
    {
        TestTransportConnection connection = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            LoginConnectionSession
                .OpenAsync(connection, null!, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(0, connection.DisposeCount);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_SendsVerifiedEncryptedSeedBeforeReturning()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(VerifiedSeed, session.LoginSeed);

        Assert.Equal(1, seedGenerator.GenerateCount);

        Assert.Equal(Convert.FromHexString("C54869128E317C0F"), connection.SentBytes);
    }

    [Fact]
    public async Task OpenAsync_WaitsForSeedTransportSendToComplete()
    {
        TestTransportConnection connection = new(blockSends: true);

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        Task<LoginConnectionSession> openTask = LoginConnectionSession
            .OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken)
            .AsTask();

        await connection.SendStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(openTask.IsCompleted);

        connection.ReleaseSend();

        await using LoginConnectionSession session = await openTask;

        Assert.Equal(Convert.FromHexString("C54869128E317C0F"), connection.SentBytes);
    }

    [Fact]
    public async Task OpenAsync_PreCanceledOperationDisposesConnectionWithoutStartingSession()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LoginConnectionSession.OpenAsync(connection, seedGenerator, cancellation.Token).AsTask()
        );

        Assert.Equal(0, seedGenerator.GenerateCount);

        Assert.Equal(0, connection.ReceiveCallCount);

        Assert.Empty(connection.SentBytes);

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task OpenAsync_OutputFailurePreservesTransportExceptionAndDisposesConnection()
    {
        IOException failure = new("send failed");

        TestTransportConnection connection = new(sendFailure: failure);

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            LoginConnectionSession
                .OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Same(failure, exception);

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task OpenAsync_OutputAndCleanupFailurePreservesBothFailures()
    {
        IOException sendFailure = new("send failed");

        InvalidOperationException disposeFailure = new("dispose failed");

        TestTransportConnection connection = new(
            sendFailure: sendFailure,
            disposeFailure: disposeFailure
        );

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            LoginConnectionSession
                .OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(2, exception.InnerExceptions.Count);

        Assert.Same(sendFailure, exception.InnerExceptions[0]);

        Assert.Same(disposeFailure, exception.InnerExceptions[1]);

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task OpenAsync_InputEndOfStreamBeforeSeedSendCompletesFailsAndDisposesConnection()
    {
        TestTransportConnection connection = new(blockSends: true);

        connection.QueueEndOfStream();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        EndOfStreamException exception = await Assert.ThrowsAsync<EndOfStreamException>(() =>
            LoginConnectionSession
                .OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(
            "The login connection input pump completed before the initial handshake finished.",
            exception.Message
        );

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task OpenAsync_InputFailureBeforeSeedSendCompletesPreservesFailureAndDisposesConnection()
    {
        IOException failure = new("receive failed during open");

        TestTransportConnection connection = new(blockSends: true);

        connection.QueueReceiveFailure(failure);

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            LoginConnectionSession
                .OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Same(failure, exception);

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task ReadAsync_DecryptsInboundFrameThroughTransportPump()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(Convert.FromHexString("D48487651720B0C3"));

        using LoginInboundFrame frame = Assert.IsType<LoginInboundFrame>(
            await session.ReadAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(LoginSeedPacket.PacketIdentifier, frame.PacketId);

        Assert.Equal(Convert.FromHexString("78563412"), frame.Payload.ToArray());
    }

    [Fact]
    public async Task ReadAsync_PropagatesTransportReceiveFailure()
    {
        IOException failure = new("receive failed");

        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        connection.QueueReceiveFailure(failure);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            session.ReadAsync(TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ReadAsync_CleanPeerEndOfStreamReturnsNull()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        connection.QueueEndOfStream();

        LoginInboundFrame? frame = await session.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Null(frame);
    }

    [Fact]
    public async Task WriteAsync_PreservesOutboundCipherStateAfterSeed()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        await session.WriteAsync(
            new LoginSeedPacket(VerifiedSeed),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            Convert.FromHexString("C54869128E317C0F" + "7DF0011AC6D914D7"),
            connection.SentBytes
        );
    }

    [Fact]
    public async Task WriteAsync_PropagatesTransportFailureAfterOpening()
    {
        TestTransportConnection connection = new();
        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(VerifiedSeed), TestContext.Current.CancellationToken);
        IOException failure = new("send failed after opening");
        connection.FailSends(failure);

        IOException observed = await Assert.ThrowsAsync<IOException>(() => session.WriteAsync(
            new LoginSeedPacket(VerifiedSeed), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task ReadAsync_PendingPartialFramePropagatesTransportFailure()
    {
        TestTransportConnection connection = new();
        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(VerifiedSeed), TestContext.Current.CancellationToken);
        Task<LoginInboundFrame?> read = session.ReadAsync(TestContext.Current.CancellationToken).AsTask();
        connection.QueueReceive(Convert.FromHexString("D4848765"));
        IOException failure = new("receive failed in credential frame");
        connection.QueueReceiveFailure(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => read));
    }

    [Fact]
    public async Task DisposeAsync_CancelsOutstandingInputAndDisposesConnectionOnce()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        await session.DisposeAsync();

        await connection.ReceiveCancellationObserved.WaitAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, connection.DisposeCount);

        await session.DisposeAsync();

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallersAwaitSameCleanup()
    {
        TestTransportConnection connection = new(blockDispose: true);

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        Task firstDispose = session.DisposeAsync().AsTask();

        await connection.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Task secondDispose = session.DisposeAsync().AsTask();

        Assert.Same(firstDispose, secondDispose);

        Assert.False(firstDispose.IsCompleted);

        Assert.Equal(1, connection.DisposeCount);

        connection.ReleaseDispose();

        await firstDispose;

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallersObserveSameCleanupFailure()
    {
        InvalidOperationException disposeFailure = new("dispose failed");

        TestTransportConnection connection = new(
            blockDispose: true,
            disposeFailure: disposeFailure
        );

        FakeLoginSeedGenerator seedGenerator = new(VerifiedSeed);

        LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            seedGenerator,
            TestContext.Current.CancellationToken
        );

        Task firstDispose = session.DisposeAsync().AsTask();

        await connection.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Task secondDispose = session.DisposeAsync().AsTask();

        Assert.Same(firstDispose, secondDispose);

        connection.ReleaseDispose();

        AggregateException firstException = await Assert.ThrowsAsync<AggregateException>(() =>
            firstDispose
        );

        AggregateException secondException = await Assert.ThrowsAsync<AggregateException>(() =>
            secondDispose
        );

        Assert.Same(firstException, secondException);

        Assert.Same(disposeFailure, Assert.Single(firstException.InnerExceptions));

        Assert.Equal(1, connection.DisposeCount);
    }

    private sealed class FakeLoginSeedGenerator(uint seed) : ILoginSeedGenerator
    {
        public int GenerateCount { get; private set; }

        public uint GenerateSeed()
        {
            GenerateCount++;

            return seed;
        }
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Channel<ReceiveOperation> _receiveOperations =
            Channel.CreateUnbounded<ReceiveOperation>();

        private readonly ArrayBufferWriter<byte> _sent = new();

        private Exception? _sendFailure;
        private readonly Exception? _disposeFailure;
        private readonly bool _blockSends;
        private readonly bool _blockDispose;

        private readonly TaskCompletionSource _sendStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly TaskCompletionSource _sendRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly TaskCompletionSource _receiveCancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly TaskCompletionSource _disposeStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly TaskCompletionSource _disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private int _disposeCount;
        private int _receiveCallCount;

        public TestTransportConnection(
            Exception? sendFailure = null,
            Exception? disposeFailure = null,
            bool blockSends = false,
            bool blockDispose = false
        )
        {
            _sendFailure = sendFailure;
            _disposeFailure = disposeFailure;
            _blockSends = blockSends;
            _blockDispose = blockDispose;
        }

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 40000);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int ReceiveCallCount => Volatile.Read(ref _receiveCallCount);

        public byte[] SentBytes => _sent.WrittenSpan.ToArray();

        public Task SendStarted => _sendStarted.Task;

        public Task ReceiveCancellationObserved => _receiveCancellationObserved.Task;

        public Task DisposeStarted => _disposeStarted.Task;

        public void FailSends(Exception failure) => _sendFailure = failure;

        public async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _receiveCallCount);

            ReceiveOperation operation;

            try
            {
                operation = await _receiveOperations
                    .Reader.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _receiveCancellationObserved.TrySetResult();

                throw;
            }

            if (operation.Failure is not null)
            {
                throw operation.Failure;
            }

            if (operation.IsEndOfStream)
            {
                return 0;
            }

            byte[] bytes =
                operation.Bytes
                ?? throw new InvalidOperationException("Receive operation does not contain bytes.");

            if (bytes.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    "Test receive does not fit the supplied transport buffer."
                );
            }

            bytes.CopyTo(buffer);

            return bytes.Length;
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            _sendStarted.TrySetResult();

            if (_blockSends)
            {
                await _sendRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_sendFailure is not null)
            {
                throw _sendFailure;
            }

            Span<byte> destination = _sent.GetSpan(buffer.Length);

            buffer.Span.CopyTo(destination);

            _sent.Advance(buffer.Length);
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);

            _disposeStarted.TrySetResult();

            if (_blockDispose)
            {
                await _disposeRelease.Task.ConfigureAwait(false);
            }

            if (_disposeFailure is not null)
            {
                throw _disposeFailure;
            }
        }

        public void QueueReceive(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(bytes, Failure: null, IsEndOfStream: false)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test receive.");
            }
        }

        public void QueueReceiveFailure(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(Bytes: null, failure, IsEndOfStream: false)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test receive failure.");
            }
        }

        public void QueueEndOfStream()
        {
            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(Bytes: null, Failure: null, IsEndOfStream: true)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test end of stream.");
            }
        }

        public void ReleaseSend()
        {
            _sendRelease.TrySetResult();
        }

        public void ReleaseDispose()
        {
            _disposeRelease.TrySetResult();
        }

        private readonly record struct ReceiveOperation(
            byte[]? Bytes,
            Exception? Failure,
            bool IsEndOfStream
        );
    }
}
