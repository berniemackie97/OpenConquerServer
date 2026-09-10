using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.GameServer.Tests.Handshake;

public sealed class GameTransportHandshakeProcessorTests
{
    [Fact]
    public async Task TryCompleteAsync_ValidKeyExchangeTransitionsSessionToSecuredTransport()
    {
        FakeGameTransportConnection transport = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameTransportHandshakeProcessor processor = new();

        Task<bool> handshake = processor.TryCompleteAsync(session, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);

        transport.QueueReceive(client.EncryptedKeyExchangeResponse);

        Assert.True(await handshake);
        Assert.Equal(0, transport.DisposeCount);

        byte[] packet = BuildPacket(1052, [0x11, 0x22, 0x33, 0x44]);
        transport.QueueReceive(client.EncryptClientFrame(packet));

        using GameInboundFrame frame = Assert.IsType<GameInboundFrame>(await session.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(packet, frame.Packet.ToArray());
    }

    [Fact]
    public async Task TryCompleteAsync_CleanPeerEndOfStreamReturnsFalseWithoutDisposingSession()
    {
        FakeGameTransportConnection transport = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameTransportHandshakeProcessor processor = new();

        transport.QueueEndOfStream();

        bool completed = await processor.TryCompleteAsync(session, TestContext.Current.CancellationToken);

        Assert.False(completed);
        Assert.NotEmpty(transport.SentBytes);
        Assert.Equal(0, transport.DisposeCount);
    }

    [Fact]
    public async Task TryCompleteAsync_PreCanceledOperationDoesNotMutateOrDisposeSession()
    {
        FakeGameTransportConnection transport = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameTransportHandshakeProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.TryCompleteAsync(session, cancellation.Token).AsTask());

        Assert.Empty(transport.SentBytes);
        Assert.Equal(0, transport.DisposeCount);

        Task<bool> retry = processor.TryCompleteAsync(session, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);

        transport.QueueReceive(client.EncryptedKeyExchangeResponse);

        Assert.True(await retry);
        Assert.Equal(0, transport.DisposeCount);
    }

    [Fact]
    public async Task TryCompleteAsync_MalformedKeyExchangePropagatesProtocolFailureWithoutDisposingSession()
    {
        FakeGameTransportConnection transport = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameTransportHandshakeProcessor processor = new();

        Task<bool> handshake = processor.TryCompleteAsync(session, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        byte[] malformedResponse = client.EncryptedKeyExchangeResponse.ToArray();
        malformedResponse[^1] ^= 0xFF;

        transport.QueueReceive(malformedResponse);

        await Assert.ThrowsAsync<InvalidDataException>(() => handshake);

        Assert.Equal(0, transport.DisposeCount);
    }

    private static byte[] BuildPacket(ushort packetId, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[WireFrameHeader.Size + payload.Length];
        WireFrameHeader.Write(packet, checked((ushort)packet.Length), packetId);
        payload.CopyTo(packet.AsSpan(WireFrameHeader.Size));
        return packet;
    }
}
