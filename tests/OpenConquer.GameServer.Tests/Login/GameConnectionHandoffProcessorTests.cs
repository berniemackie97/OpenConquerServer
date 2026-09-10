using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class GameConnectionHandoffProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");

    [Fact]
    public async Task ProcessAsync_CoalescedHandshakeResponseAndLoginProofReturnsLiveAuthenticatedConnection()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid) };
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);
        AuthenticatedGameConnection? authenticatedConnection = null;

        Task<GameConnectionAuthenticationResult> processing = processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        byte[] securedLoginProof = client.EncryptClientFrame(BuildLoginProof());

        transport.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. securedLoginProof]);

        try
        {
            GameConnectionAuthenticationResult result = await processing;
            authenticatedConnection = result.Connection;

            Assert.Equal(GameConnectionAuthenticationStatus.Authenticated, result.Status);

            AuthenticatedGameConnection connection = Assert.IsType<AuthenticatedGameConnection>(authenticatedConnection);

            Assert.Equal(AccountId, connection.AccountId);
            Assert.Equal(Username, connection.Username);
            Assert.Equal(SessionUid, connection.SessionUid);
            Assert.Equal(LocaleTag, connection.LocaleTag);
            Assert.Equal(HardwareAddress, connection.HardwareAddress);
            Assert.Equal(ResourceVersion, connection.ResourceVersion);
            Assert.Equal(transport.LocalEndPoint, connection.LocalEndPoint);
            Assert.Equal(transport.RemoteEndPoint, connection.RemoteEndPoint);

            Assert.Equal(1, store.RedemptionCount);
            Assert.Equal(SessionUid, store.LastSessionUid);
            Assert.Equal(AuthenticationKey, store.LastAuthenticationKey);
            Assert.Equal(1, limiter.BeginCount);
            Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
            Assert.Equal(SessionUid, limiter.LastSessionUid);
            Assert.Equal(0, transport.DisposeCount);
        }
        finally
        {
            if (authenticatedConnection is not null)
            {
                await authenticatedConnection.DisposeAsync();
            }
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperationDisposesAcceptedTransportWithoutStartingSessionPumps()
    {
        FakeGameTransportConnection transport = new();
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(transport, cancellation.Token).AsTask());

        Assert.Equal(0, transport.ReceiveCallCount);
        Assert.Empty(transport.SentBytes);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, store.RedemptionCount);
        Assert.Equal(0, limiter.BeginCount);
    }

    [Fact]
    public async Task ProcessAsync_CleanPeerEndOfStreamBeforeKeyExchangeReturnsPeerClosedAndDisposesSession()
    {
        FakeGameTransportConnection transport = new();
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);

        transport.QueueEndOfStream();

        GameConnectionAuthenticationResult result = await processor.ProcessAsync(transport, TestContext.Current.CancellationToken);

        Assert.Equal(GameConnectionAuthenticationStatus.PeerClosed, result.Status);
        Assert.Null(result.Connection);
        Assert.NotEmpty(transport.SentBytes);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, store.RedemptionCount);
        Assert.Equal(0, limiter.BeginCount);
    }

    [Fact]
    public async Task ProcessAsync_UnredeemableLoginProofReturnsAuthorizationRejectedAndDisposesSession()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);

        Task<GameConnectionAuthenticationResult> processing = processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);

        transport.QueueReceive(
        [
            .. client.EncryptedKeyExchangeResponse,
            .. client.EncryptClientFrame(BuildLoginProof()),
        ]);

        GameConnectionAuthenticationResult result = await processing;

        Assert.Equal(GameConnectionAuthenticationStatus.AuthorizationRejected, result.Status);
        Assert.Null(result.Connection);
        Assert.Equal(1, store.RedemptionCount);
        Assert.Equal(SessionUid, store.LastSessionUid);
        Assert.Equal(AuthenticationKey, store.LastAuthenticationKey);
        Assert.Equal(1, limiter.BeginCount);
        Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_MalformedKeyExchangeResponsePropagatesProtocolFailureAndDisposesSession()
    {
        FakeGameTransportConnection transport = new();
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);

        Task<GameConnectionAuthenticationResult> processing = processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        using GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        byte[] malformedResponse = client.EncryptedKeyExchangeResponse.ToArray();
        malformedResponse[^1] ^= 0xFF;

        transport.QueueReceive(malformedResponse);

        await Assert.ThrowsAsync<InvalidDataException>(() => processing);

        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, store.RedemptionCount);
        Assert.Equal(0, limiter.BeginCount);
    }

    [Fact]
    public async Task ProcessAsync_HandshakeFailureAndCleanupFailureAreAggregated()
    {
        IOException processingFailure = new("transport receive failed");
        InvalidOperationException cleanupFailure = new("transport dispose failed");
        FakeGameTransportConnection transport = new(disposeFailure: cleanupFailure);
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateProcessor(store, limiter);

        transport.QueueReceiveFailure(processingFailure);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(processingFailure, exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, store.RedemptionCount);
        Assert.Equal(0, limiter.BeginCount);
    }

    private static GameConnectionHandoffProcessor CreateProcessor(FakeRedemptionStore store, FakeAttemptLimiter limiter)
    {
        GameLoginTicketRedeemer redeemer = new(store, limiter);
        GameConnectionAuthenticator authenticator = new(redeemer);

        return new GameConnectionHandoffProcessor(new GameTransportHandshakeProcessor(), authenticator);
    }

    private static byte[] BuildLoginProof()
    {
        byte[] packet = new byte[GameLoginProof1052.PacketLength];

        WireFrameHeader.Write(packet, GameLoginProof1052.PacketLength, GameLoginProof1052.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), SessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), AuthenticationKey);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), GameLoginProof1052.ExpectedMode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), LocaleTag);
        packet[16] = 0x11;
        packet[17] = 0x22;
        packet[18] = 0x33;
        packet[19] = 0x44;
        packet[20] = 0x55;
        packet[21] = 0x66;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24), ResourceVersion);

        return packet;
    }

    private sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
    {
        public GameLoginTicketIdentity? Result { get; init; }
        public int RedemptionCount { get; private set; }
        public uint? LastSessionUid { get; private set; }
        public uint? LastAuthenticationKey { get; private set; }

        public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RedemptionCount++;
            LastSessionUid = sessionUid;
            LastAuthenticationKey = authenticationKey;

            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakeAttemptLimiter : IGameLoginTicketRedemptionAttemptLimiter
    {
        public int BeginCount { get; private set; }
        public IPAddress? LastRemoteAddress { get; private set; }
        public uint LastSessionUid { get; private set; }

        public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attempt)
        {
            BeginCount++;
            LastRemoteAddress = remoteAddress;
            LastSessionUid = sessionUid;
            attempt = new FakeAttemptLease();
            return true;
        }
    }

    private sealed class FakeAttemptLease : IGameLoginTicketRedemptionAttemptLease
    {
        public void Complete(bool authorizationAccepted)
        {
        }

        public void Dispose()
        {
        }
    }
}
