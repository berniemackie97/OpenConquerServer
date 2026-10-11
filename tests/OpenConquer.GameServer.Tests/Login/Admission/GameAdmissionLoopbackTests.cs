using System.Net;
using System.Net.Sockets;
using OpenConquer.GameServer.Login.Admission;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.Transport.Connections;
using OpenConquer.Transport.Sockets;

namespace OpenConquer.GameServer.Tests.Login.Admission;

public sealed class GameAdmissionLoopbackTests
{
    [Fact]
    public async Task AcceptAsync_RealTcpConnectionTransfersTransportOwnership()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        cancellation.CancelAfter(TimeSpan.FromSeconds(10));

        await using SocketTransportListener transport = new(
            new IPEndPoint(IPAddress.Loopback, 0), backlog: 16);

        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 2, metrics, static _ => { });

        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        await client.ConnectAsync(transport.LocalEndPoint, cancellation.Token);

        ITransportConnection accepted = await listener.AcceptAsync(cancellation.Token);

        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)accepted.RemoteEndPoint).Address);
        Assert.Equal(transport.LocalEndPoint.Port, ((IPEndPoint)accepted.LocalEndPoint).Port);

        byte[] payload = [0x12, 0x34, 0x56, 0x78];

        await client.SendAsync(payload, SocketFlags.None, cancellation.Token);

        byte[] received = new byte[payload.Length];
        int offset = 0;

        while (offset < received.Length)
        {
            int count = await accepted.ReceiveAsync(received.AsMemory(offset), cancellation.Token);

            Assert.True(count > 0);
            offset += count;
        }

        Assert.Equal(payload, received);

        await accepted.DisposeAsync();
        await accepted.DisposeAsync();
    }
}
