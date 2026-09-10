using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class GameConnectionAuthenticatorTests
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
    public async Task AuthenticateAsync_ValidLoginProofTransfersLiveAuthenticatedConnection()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid), AcceptedAuthenticationKey = AuthenticationKey };
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);
        AuthenticatedGameConnection? authenticatedConnection = null;

        try
        {
            byte[] loginProof = BuildLoginProof();
            byte[] nextPacket = BuildPacket(1004, [0x11, 0x22, 0x33, 0x44]);
            transport.QueueReceive([.. client.EncryptClientFrame(loginProof), .. client.EncryptClientFrame(nextPacket)]);

            GameConnectionAuthenticationResult result = await authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken);
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
            Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
            Assert.Equal(SessionUid, limiter.LastSessionUid);
            Assert.Equal(0, transport.DisposeCount);

            using GameInboundFrame nextFrame = Assert.IsType<GameInboundFrame>(await connection.ReadAsync(TestContext.Current.CancellationToken));

            Assert.Equal(nextPacket, nextFrame.Packet.ToArray());
        }
        finally
        {
            if (authenticatedConnection is not null)
            {
                await authenticatedConnection.DisposeAsync();
            }

            client.Dispose();
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongAuthenticationKeyIsRejectedAndSessionDisposed()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid), AcceptedAuthenticationKey = AuthenticationKey };
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            uint wrongAuthenticationKey = AuthenticationKey + 1;
            transport.QueueReceive(client.EncryptClientFrame(BuildLoginProof(authenticationKey: wrongAuthenticationKey)));

            GameConnectionAuthenticationResult result = await authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken);

            Assert.Equal(GameConnectionAuthenticationStatus.AuthorizationRejected, result.Status);
            Assert.Null(result.Connection);
            Assert.Equal(1, store.RedemptionCount);
            Assert.Equal(SessionUid, store.LastSessionUid);
            Assert.Equal(wrongAuthenticationKey, store.LastAuthenticationKey);
            Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_PeerClosesBeforeLoginProofReturnsPeerClosedAndDisposesSession()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            transport.QueueEndOfStream();

            GameConnectionAuthenticationResult result = await authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken);

            Assert.Equal(GameConnectionAuthenticationStatus.PeerClosed, result.Status);
            Assert.Null(result.Connection);
            Assert.Equal(0, store.RedemptionCount);
            Assert.Equal(0, limiter.BeginCount);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidFirstSecuredPacketThrowsAndDisposesSession()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            transport.QueueReceive(client.EncryptClientFrame(BuildPacket(1004, new byte[24])));

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken).AsTask());

            Assert.Contains(nameof(GameLoginProofParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, store.RedemptionCount);
            Assert.Equal(0, limiter.BeginCount);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_UnsupportedRemoteEndpointThrowsAndDisposesSession()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new DnsEndPoint("example.test", 40000));
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(0, store.RedemptionCount);
            Assert.Equal(0, limiter.BeginCount);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_PreCanceledOperationDisposesOwnedSessionWithoutRedemption()
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authenticator.AuthenticateAsync(session, cancellation.Token).AsTask());

            Assert.Equal(0, store.RedemptionCount);
            Assert.Equal(0, limiter.BeginCount);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_RedemptionFailurePropagatesAndDisposesSession()
    {
        IOException failure = new("redemption failed");
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000));
        FakeRedemptionStore store = new() { Exception = failure };
        FakeAttemptLimiter limiter = new();
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(store, limiter));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            transport.QueueReceive(client.EncryptClientFrame(BuildLoginProof()));

            IOException observed = await Assert.ThrowsAsync<IOException>(() =>
                authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken).AsTask());

            Assert.Same(failure, observed);
            Assert.Equal(1, store.RedemptionCount);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_AuthenticationAndCleanupFailuresAreAggregated()
    {
        InvalidOperationException cleanupFailure = new("transport dispose failed");
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: cleanupFailure);
        GameConnectionAuthenticator authenticator = new(new GameLoginTicketRedeemer(new FakeRedemptionStore(), new FakeAttemptLimiter()));
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);

        try
        {
            transport.QueueReceive(client.EncryptClientFrame(BuildPacket(1004, new byte[24])));

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
                authenticator.AuthenticateAsync(session, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
            Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
            Assert.Equal(1, transport.DisposeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task<(GameConnectionSession Session, GameClientTestPeer Client)> OpenSecuredSessionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        try
        {
            await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);
            transport.QueueReceive(client.EncryptedKeyExchangeResponse);

            string clientPublicKeyHex = Assert.IsType<string>(await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
            Assert.Equal(client.PublicKeyHex, clientPublicKeyHex);

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

            return (session, client);
        }
        catch
        {
            client.Dispose();
            await session.DisposeAsync();
            throw;
        }
    }

    private static byte[] BuildLoginProof(uint authenticationKey = AuthenticationKey)
    {
        byte[] packet = new byte[GameLoginProof1052.PacketLength];

        WireFrameHeader.Write(packet, GameLoginProof1052.PacketLength, GameLoginProof1052.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), SessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), authenticationKey);
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

    private static byte[] BuildPacket(ushort packetId, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[WireFrameHeader.Size + payload.Length];
        WireFrameHeader.Write(packet, checked((ushort)packet.Length), packetId);
        payload.CopyTo(packet.AsSpan(WireFrameHeader.Size));
        return packet;
    }

    private sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
    {
        public GameLoginTicketIdentity? Result { get; init; }
        public uint? AcceptedAuthenticationKey { get; init; }
        public Exception? Exception { get; init; }
        public int RedemptionCount { get; private set; }
        public uint? LastSessionUid { get; private set; }
        public uint? LastAuthenticationKey { get; private set; }

        public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RedemptionCount++;
            LastSessionUid = sessionUid;
            LastAuthenticationKey = authenticationKey;

            if (Exception is not null)
            {
                throw Exception;
            }

            GameLoginTicketIdentity? result = AcceptedAuthenticationKey is uint expected && authenticationKey != expected ? null : Result;
            return ValueTask.FromResult(result);
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
