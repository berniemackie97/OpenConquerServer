using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.Tests.Connections;

public sealed class GameConnectionSessionTests
{
    [Fact]
    public async Task OpenAsync_PreCanceledOperationDisposesConnectionWithoutStartingPumps()
    {
        FakeGameTransportConnection connection = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameConnectionSession.OpenAsync(connection, cancellation.Token).AsTask());

        Assert.Equal(0, connection.ReceiveCallCount);
        Assert.Empty(connection.SentBytes);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task SendHandshakeChallengeAsync_WritesExactChallenge()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        Assert.Equal(exchange.EncryptedChallenge.ToArray(), connection.SentBytes);
    }

    [Fact]
    public async Task ReadClientKeyExchangeResponseAsync_ReassemblesFragmentedTransportResponse()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        using GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        Task<string?> readTask = session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken).AsTask();

        connection.QueueReceive(client.EncryptedKeyExchangeResponse.AsSpan(0, 5));
        connection.QueueReceive(client.EncryptedKeyExchangeResponse.AsSpan(5, 6));
        connection.QueueReceive(client.EncryptedKeyExchangeResponse.AsSpan(11));

        Assert.Equal(client.PublicKeyHex, await readTask);
    }

    [Fact]
    public async Task ReadClientKeyExchangeResponseAsync_CleanPeerEndOfStreamReturnsNull()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        connection.QueueEndOfStream();

        Assert.Null(await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadClientKeyExchangeResponseAsync_TruncatedPeerEndOfStreamThrowsEndOfStreamException()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        using GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        connection.QueueReceive(client.EncryptedKeyExchangeResponse.AsSpan(0, client.EncryptedKeyExchangeResponse.Length - 1));
        connection.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DisposeAsync_DrainsActiveKeyExchangeReadBeforeDisposingTransport()
    {
        FakeGameTransportConnection connection = new(blockDispose: true);
        GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        Task<string?> readTask = session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.False(readTask.IsCompleted);

        Task disposeTask = session.DisposeAsync().AsTask();

        await connection.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(readTask.IsCompleted);

        connection.ReleaseDispose();

        await disposeTask;

        string? result = null;
        Exception? failure = await Record.ExceptionAsync(async () => result = await readTask);

        Assert.Null(result);
        Assert.True(failure is null or OperationCanceledException);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task CompleteHandshake_PreservesCoalescedFirstSecuredFrame()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        using GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        byte[] packet = BuildPacket(1052, [0x11, 0x22, 0x33, 0x44]);
        byte[] securedFrame = client.EncryptClientFrame(packet);

        connection.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. securedFrame]);

        string clientPublicKeyHex = Assert.IsType<string>(
            await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));

        Assert.Equal(client.PublicKeyHex, clientPublicKeyHex);

        TransferSessionCipher(exchange, session, clientPublicKeyHex);

        using GameInboundFrame frame = Assert.IsType<GameInboundFrame>(
            await session.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(packet, frame.Packet.ToArray());
    }

    [Fact]
    public async Task ReadAsync_TruncatedSecuredFrameThrowsEndOfStreamException()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        byte[] encryptedFrame = client.EncryptClientFrame(BuildPacket(1052, [0x11, 0x22, 0x33]));

        connection.QueueReceive(encryptedFrame.AsSpan(0, encryptedFrame.Length - 1));
        connection.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            session.ReadAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ReadAsync_InvalidSecuredSignatureThrowsInvalidDataExceptionAndTerminalizesReader()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        byte[] encryptedFrame = client.EncryptClientFrame(BuildPacket(1052, [0x11, 0x22, 0x33, 0x44]));
        encryptedFrame[^1] ^= 0xFF;

        connection.QueueReceive(encryptedFrame);

        await Assert.ThrowsAsync<InvalidDataException>(() => session.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WriteAsync_EncryptsCompleteServerFrameForClient()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        int securedOffset = connection.SentBytes.Length;
        TestPacket packet = new(1004, [0x10, 0x20, 0x30, 0x40]);

        await session.WriteAsync(packet, TestContext.Current.CancellationToken);

        byte[] encrypted = connection.SentBytes[securedOffset..];
        byte[] plaintext = client.DecryptServerBytes(encrypted);
        byte[] expectedPacket = BuildPacket(packet.PacketId, packet.Payload);

        Assert.Equal(expectedPacket.Length + GameWireProtocol.SignatureLength, plaintext.Length);
        Assert.Equal(expectedPacket, plaintext.AsSpan(0, expectedPacket.Length).ToArray());
        Assert.True(plaintext.AsSpan(expectedPacket.Length).SequenceEqual(GameWireProtocol.ServerSignature));
    }

    [Fact]
    public async Task WriteAsync_ConcurrentWritersPreserveCompleteSerializedFrames()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        int securedOffset = connection.SentBytes.Length;
        TestPacket[] packets = Enumerable.Range(0, 16)
            .Select(index => new TestPacket((ushort)(1200 + index), [(byte)index, 0xA5, 0x5A]))
            .ToArray();

        Task[] writes = packets
            .Select(packet => session.WriteAsync(packet, TestContext.Current.CancellationToken).AsTask())
            .ToArray();

        await Task.WhenAll(writes);

        byte[] plaintext = client.DecryptServerBytes(connection.SentBytes[securedOffset..]);
        List<byte[]> frames = ParseServerFrames(plaintext);
        Dictionary<ushort, byte[]> expected = packets.ToDictionary(packet => packet.PacketId, packet => BuildPacket(packet.PacketId, packet.Payload));
        HashSet<ushort> observed = [];

        Assert.Equal(packets.Length, frames.Count);

        foreach (byte[] frame in frames)
        {
            Assert.True(WireFrameHeader.TryRead(frame, out WireFrameHeader header));
            Assert.True(observed.Add(header.PacketId));
            Assert.Equal(expected[header.PacketId], frame);
        }

        Assert.Equal(expected.Count, observed.Count);
    }

    [Fact]
    public async Task DisposeAsync_CancelsPublishedWriteAndPreventsQueuedWritePublication()
    {
        FakeGameTransportConnection connection = new();
        await using GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        int securedOffset = connection.SentBytes.Length;

        connection.BlockSends();

        Task firstWrite = session.WriteAsync(
            new TestPacket(1201, [0x11, 0x22, 0x33]),
            TestContext.Current.CancellationToken).AsTask();

        await connection.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        Task secondWrite = session.WriteAsync(
            new TestPacket(1202, [0x44, 0x55, 0x66]),
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(firstWrite.IsCompleted);
        Assert.False(secondWrite.IsCompleted);

        await session.DisposeAsync();

        Exception? firstFailure = await Record.ExceptionAsync(() => firstWrite);
        Exception? secondFailure = await Record.ExceptionAsync(() => secondWrite);

        Assert.NotNull(firstFailure);
        Assert.NotNull(secondFailure);
        Assert.Equal(securedOffset, connection.SentBytes.Length);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_RejectsNewSecuredOperationsAfterDisposalBegins()
    {
        FakeGameTransportConnection connection = new(blockDispose: true);
        GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);
        using GameClientTestPeer client = await CompleteHandshakeAsync(session, connection);

        Task disposeTask = session.DisposeAsync().AsTask();

        await connection.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.WriteAsync(new TestPacket(1201, [0x11, 0x22, 0x33]), TestContext.Current.CancellationToken).AsTask());

        connection.ReleaseDispose();

        await disposeTask;

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_CancelsOutstandingInputAndDisposesConnectionOnce()
    {
        FakeGameTransportConnection connection = new();
        GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);

        await connection.ReceiveStarted.WaitAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();
        await connection.ReceiveCancellationObserved.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, connection.DisposeCount);

        await session.DisposeAsync();

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallersAwaitSameCleanup()
    {
        FakeGameTransportConnection connection = new(blockDispose: true);
        GameConnectionSession session = await GameConnectionSession.OpenAsync(connection, TestContext.Current.CancellationToken);

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

    private static async Task<GameClientTestPeer> CompleteHandshakeAsync(GameConnectionSession session, FakeGameTransportConnection connection)
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        try
        {
            await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

            connection.QueueReceive(client.EncryptedKeyExchangeResponse);

            string clientPublicKeyHex = Assert.IsType<string>(
                await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));

            Assert.Equal(client.PublicKeyHex, clientPublicKeyHex);

            TransferSessionCipher(exchange, session, clientPublicKeyHex);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static void TransferSessionCipher(GameHandshakeExchange exchange, GameConnectionSession session, string clientPublicKeyHex)
    {
        GameSessionCipher? sessionCipher = exchange.Complete(clientPublicKeyHex);

        try
        {
            session.CompleteHandshake(sessionCipher);
            sessionCipher = null;
        }
        finally
        {
            sessionCipher?.Dispose();
        }
    }

    private static byte[] BuildPacket(ushort packetId, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[WireFrameHeader.Size + payload.Length];
        WireFrameHeader.Write(packet, checked((ushort)packet.Length), packetId);
        payload.CopyTo(packet.AsSpan(WireFrameHeader.Size));
        return packet;
    }

    private static List<byte[]> ParseServerFrames(ReadOnlySpan<byte> plaintext)
    {
        List<byte[]> frames = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            if (!WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header) || header.Length < WireFrameHeader.Size)
            {
                throw new InvalidDataException("Decrypted server output does not contain a valid frame header.");
            }

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);

            if (wireLength > plaintext.Length - offset)
            {
                throw new InvalidDataException("Decrypted server output contains a truncated frame.");
            }

            if (!plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature))
            {
                throw new InvalidDataException("Decrypted server output contains an invalid server signature.");
            }

            frames.Add(plaintext.Slice(offset, header.Length).ToArray());
            offset += wireLength;
        }

        return frames;
    }

    private sealed class TestPacket(ushort packetId, byte[] payload) : IPacket
    {
        public ushort PacketId { get; } = packetId;
        public byte[] Payload { get; } = payload ?? throw new ArgumentNullException(nameof(payload));
        public int PayloadLength => Payload.Length;

        public void WritePayload(ref PacketWriter writer) => writer.WriteBytes(Payload);
    }
}
