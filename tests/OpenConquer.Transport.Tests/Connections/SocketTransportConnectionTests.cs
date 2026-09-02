using System.Net;
using System.Net.Sockets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class SocketTransportConnectionTests
{
    [Fact]
    public void Constructor_RejectsNullSocket()
    {
        Assert.Throws<ArgumentNullException>(() => new SocketTransportConnection(null!));
    }

    [Fact]
    public void Constructor_RejectsAndDisposesNonStreamSocket()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Assert.Throws<ArgumentException>(() => new SocketTransportConnection(socket));

        Assert.True(socket.SafeHandle.IsClosed);
    }

    [Fact]
    public void Constructor_RejectsAndDisposesUnestablishedStreamSocket()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        Assert.Throws<ArgumentException>(() => new SocketTransportConnection(socket));

        Assert.True(socket.SafeHandle.IsClosed);
    }

    [Fact]
    public async Task Constructor_CapturesEndpointsForConnectionLifetime()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        {
            EndPoint expectedLocalEndPoint = server.LocalEndPoint!;
            EndPoint expectedRemoteEndPoint = server.RemoteEndPoint!;

            SocketTransportConnection connection = new(server);

            Assert.Equal(expectedLocalEndPoint, connection.LocalEndPoint);
            Assert.Equal(expectedRemoteEndPoint, connection.RemoteEndPoint);

            await connection.DisposeAsync();

            Assert.Equal(expectedLocalEndPoint, connection.LocalEndPoint);
            Assert.Equal(expectedRemoteEndPoint, connection.RemoteEndPoint);
        }
    }

    [Fact]
    public async Task ReceiveAsync_ReceivesAvailableStreamBytes()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        {
            byte[] payload = [0x11, 0x22, 0x33, 0x44];

            await client.SendAsync(
                payload,
                SocketFlags.None,
                TestContext.Current.CancellationToken
            );

            byte[] buffer = new byte[payload.Length];

            int received = await connection.ReceiveAsync(
                buffer,
                TestContext.Current.CancellationToken
            );

            Assert.InRange(received, 1, payload.Length);
            Assert.Equal(
                payload.AsSpan(0, received).ToArray(),
                buffer.AsSpan(0, received).ToArray()
            );
        }
    }

    [Fact]
    public async Task ReceiveAsync_ReturnsZeroAfterPeerGracefullyClosesSendingSide()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        {
            client.Shutdown(SocketShutdown.Send);

            int received = await connection.ReceiveAsync(
                new byte[1],
                TestContext.Current.CancellationToken
            );

            Assert.Equal(0, received);
        }
    }

    [Fact]
    public async Task ReceiveAsync_EmptyBufferReturnsZeroWithoutConsumingStreamData()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        {
            int received = await connection.ReceiveAsync(
                Memory<byte>.Empty,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(0, received);

            await client.SendAsync(
                new byte[] { 0x5A },
                SocketFlags.None,
                TestContext.Current.CancellationToken
            );

            byte[] buffer = new byte[1];

            received = await connection.ReceiveAsync(buffer, TestContext.Current.CancellationToken);

            Assert.Equal(1, received);
            Assert.Equal(0x5A, buffer[0]);
        }
    }

    [Fact]
    public async Task SendAsync_SendsCompleteBuffer()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        using (CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10)))
        {
            byte[] payload = new byte[256 * 1024];

            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)index;
            }

            Task sendTask = connection.SendAsync(payload, cancellation.Token).AsTask();

            byte[] received = new byte[payload.Length];

            await ReceiveExactlyAsync(client, received, cancellation.Token);

            await sendTask;

            Assert.Equal(payload, received);
        }
    }

    [Fact]
    public async Task SendAsync_EmptyBufferCompletes()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        {
            await connection.SendAsync(
                ReadOnlyMemory<byte>.Empty,
                TestContext.Current.CancellationToken
            );
        }
    }

    [Fact]
    public async Task ReceiveAsync_RejectsOverlappingReceive()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        using (CancellationTokenSource cancellation = new())
        {
            ValueTask<int> firstReceive = connection.ReceiveAsync(new byte[1], cancellation.Token);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await connection
                        .ReceiveAsync(new byte[1], TestContext.Current.CancellationToken)
                        .AsTask()
                );

            Assert.Contains("Only one receive operation may be active", exception.Message);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await firstReceive.AsTask()
            );
        }
    }

    [Fact]
    public async Task SendAsync_RejectsOverlappingSend()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        client.ReceiveBufferSize = 1024;
        server.SendBufferSize = 1024;

        using (client)
        await using (SocketTransportConnection connection = new(server))
        using (CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10)))
        {
            byte[] payload = new byte[4 * 1024 * 1024];

            ValueTask firstSend = connection.SendAsync(payload, cancellation.Token);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await connection
                        .SendAsync(
                            ReadOnlyMemory<byte>.Empty,
                            TestContext.Current.CancellationToken
                        )
                        .AsTask()
                );

            Assert.Contains("Only one send operation may be active", exception.Message);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await firstSend.AsTask()
            );
        }
    }

    [Fact]
    public async Task ReceiveAndSend_MayExecuteConcurrently()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        await using (SocketTransportConnection connection = new(server))
        using (CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10)))
        {
            byte[] inboundBuffer = new byte[1];

            ValueTask<int> receive = connection.ReceiveAsync(inboundBuffer, cancellation.Token);

            await connection.SendAsync(new byte[] { 0xAA }, cancellation.Token);

            byte[] outboundBuffer = new byte[1];

            await ReceiveExactlyAsync(client, outboundBuffer, cancellation.Token);

            Assert.Equal(0xAA, outboundBuffer[0]);

            await client.SendAsync(new byte[] { 0xBB }, SocketFlags.None, cancellation.Token);

            int received = await receive;

            Assert.Equal(1, received);
            Assert.Equal(0xBB, inboundBuffer[0]);
        }
    }

    [Fact]
    public async Task DisposeAsync_InterruptsActiveReceive()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        {
            SocketTransportConnection connection = new(server);

            ValueTask<int> receive = connection.ReceiveAsync(
                new byte[1],
                TestContext.Current.CancellationToken
            );

            await connection.DisposeAsync();

            Exception? exception = null;

            try
            {
                await receive;
            }
            catch (Exception caught)
            {
                exception = caught;
            }

            Assert.NotNull(exception);
            Assert.True(
                exception is ObjectDisposedException or SocketException,
                $"Expected disposal to interrupt the receive with "
                    + $"{nameof(ObjectDisposedException)} or {nameof(SocketException)}, "
                    + $"but received {exception.GetType().FullName}."
            );
        }
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndRejectsFurtherOperations()
    {
        (Socket client, Socket server) = await CreateConnectedSocketsAsync();

        using (client)
        {
            SocketTransportConnection connection = new(server);

            await connection.DisposeAsync();
            await connection.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection
                    .ReceiveAsync(new byte[1], TestContext.Current.CancellationToken)
                    .AsTask()
            );

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection
                    .SendAsync(new byte[] { 0x01 }, TestContext.Current.CancellationToken)
                    .AsTask()
            );
        }
    }

    private static async Task<(Socket Client, Socket Server)> CreateConnectedSocketsAsync()
    {
        using Socket listener = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );

        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        IPEndPoint listenerEndPoint = (IPEndPoint)listener.LocalEndPoint!;

        Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            ValueTask<Socket> accept = listener.AcceptAsync(TestContext.Current.CancellationToken);

            await client.ConnectAsync(listenerEndPoint, TestContext.Current.CancellationToken);

            Socket server = await accept;

            return (client, server);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ReceiveExactlyAsync(
        Socket socket,
        Memory<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        int received = 0;

        while (received < buffer.Length)
        {
            int count = await socket.ReceiveAsync(
                buffer[received..],
                SocketFlags.None,
                cancellationToken
            );

            if (count == 0)
            {
                throw new InvalidOperationException(
                    "Peer closed before the expected test payload was received."
                );
            }

            received += count;
        }
    }
}
