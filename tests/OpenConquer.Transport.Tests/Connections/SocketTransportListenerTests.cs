using System.Net;
using System.Net.Sockets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class SocketTransportListenerTests
{
    [Fact]
    public void Constructor_RejectsNullLocalEndPoint()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SocketTransportListener(null!, backlog: 1)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidBacklog(int backlog)
    {
        IPEndPoint localEndPoint = new(
            IPAddress.Loopback,
            port: 0
        );

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SocketTransportListener(localEndPoint, backlog)
            );

        Assert.Equal("backlog", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_BindsRequestedEndpoint()
    {
        await using SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        Assert.Equal(
            IPAddress.Loopback,
            listener.LocalEndPoint.Address
        );

        Assert.InRange(
            listener.LocalEndPoint.Port,
            1,
            ushort.MaxValue
        );
    }

    [Fact]
    public async Task AcceptAsync_ReturnsEstablishedTransportConnection()
    {
        await using SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        using Socket client = CreateClientSocket();

        Task<ITransportConnection> accept = listener
            .AcceptAsync(TestContext.Current.CancellationToken)
            .AsTask();

        await client.ConnectAsync(
            listener.LocalEndPoint,
            TestContext.Current.CancellationToken
        );

        await using ITransportConnection connection = await accept;

        Assert.Equal(
            listener.LocalEndPoint,
            connection.LocalEndPoint
        );

        Assert.Equal(
            client.LocalEndPoint,
            connection.RemoteEndPoint
        );
    }

    [Fact]
    public async Task AcceptAsync_CancellationDoesNotPoisonListener()
    {
        await using SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken
            );

        Task<ITransportConnection> canceledAccept = listener
            .AcceptAsync(cancellation.Token)
            .AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledAccept
        );

        using Socket client = CreateClientSocket();

        Task<ITransportConnection> nextAccept = listener
            .AcceptAsync(TestContext.Current.CancellationToken)
            .AsTask();

        await client.ConnectAsync(
            listener.LocalEndPoint,
            TestContext.Current.CancellationToken
        );

        await using ITransportConnection connection = await nextAccept;

        Assert.Equal(
            listener.LocalEndPoint,
            connection.LocalEndPoint
        );
    }

    [Fact]
    public async Task DisposeAsync_InterruptsActiveAccept()
    {
        SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        Task<ITransportConnection> accept = listener
            .AcceptAsync(TestContext.Current.CancellationToken)
            .AsTask();

        await listener.DisposeAsync();

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => accept
        );

        Assert.True(
            exception is ObjectDisposedException
                or SocketException
                or OperationCanceledException,
            $"Unexpected exception type: {exception.GetType().FullName}"
        );
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndRejectsFurtherAccepts()
    {
        SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        await listener.DisposeAsync();
        await listener.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () =>
                listener
                    .AcceptAsync(TestContext.Current.CancellationToken)
                    .AsTask()
        );
    }

    [Fact]
    public async Task AcceptedConnection_RemainsUsableAfterListenerDisposal()
    {
        SocketTransportListener listener = new(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );

        using Socket client = CreateClientSocket();

        Task<ITransportConnection> accept = listener
            .AcceptAsync(TestContext.Current.CancellationToken)
            .AsTask();

        await client.ConnectAsync(
            listener.LocalEndPoint,
            TestContext.Current.CancellationToken
        );

        await using ITransportConnection connection = await accept;

        await listener.DisposeAsync();

        byte[] sent = [0x11];

        int sentCount = await client.SendAsync(
            sent,
            SocketFlags.None,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, sentCount);

        byte[] received = new byte[1];

        int receivedCount = await connection.ReceiveAsync(
            received,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, receivedCount);
        Assert.Equal(0x11, received[0]);
    }

    private static Socket CreateClientSocket()
    {
        return new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
    }
}
