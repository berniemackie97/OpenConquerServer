using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterBootstrapProcessorTests
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
    public async Task ProcessAsync_ExistingCharacter_WritesVerifiedBootstrapOrderAndTransfersAwaitingEnterMapConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile(rebirthCount: 2, preRebirthLevel: 130);
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        ExistingCharacterBootstrapProcessor processor = new();
        int receiveCallCountBeforeBootstrap = fixture.Transport.ReceiveCallCount;

        AwaitingEnterMapConnection result = await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken);

        Assert.Same(fixture.Connection, result.Connection);
        Assert.Same(profile, result.Profile);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Equal(receiveCallCountBeforeBootstrap, fixture.Transport.ReceiveCallCount);

        byte[] encryptedBootstrap = fixture.Transport.SentBytes[fixture.AuthenticationBoundary..];
        byte[] plaintextBootstrap = fixture.Client.DecryptServerBytes(encryptedBootstrap);

        Assert.Equal(
            [
                GameTalkPacket1004.PacketIdentifier,
                GameLoginHistoryPacket2078.PacketIdentifier,
                GameServerStatePacket2079.PacketIdentifier,
                GameUserInfoPacket1006.PacketIdentifier,
            ],
            ReadPacketIds(plaintextBootstrap));
    }

    [Fact]
    public async Task ProcessAsync_CharacterCreationRouteIsRejectedWithoutWritingBootstrapAndConnectionIsDisposed()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.CharacterCreation());
        ExistingCharacterBootstrapProcessor processor = new();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("handoff", exception.ParamName);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperationWritesNothingAndDisposesConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(handoff, cancellation.Token).AsTask());

        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringBootstrapWriteWritesNoPartialPacketAndDisposesConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.Transport.BlockSends();

        Task<AwaitingEnterMapConnection> processing = processor.ProcessAsync(handoff, cancellation.Token).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        fixture.Transport.ReleaseSends();

        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using AuthenticatedFixture fixture = await AuthenticateAsync(disposeFailure: cleanupFailure);
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.CharacterCreation());
        ExistingCharacterBootstrapProcessor processor = new();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<ArgumentException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    private static async Task<AuthenticatedFixture> AuthenticateAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid) };
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateHandoffProcessor(store, limiter);
        Task<GameConnectionAuthenticationResult> processing = processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        int authenticationBoundary = transport.SentBytes.Length;

        try
        {
            transport.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. client.EncryptClientFrame(BuildLoginProof())]);

            GameConnectionAuthenticationResult authentication = await processing;
            AuthenticatedGameConnection connection = Assert.IsType<AuthenticatedGameConnection>(authentication.Connection);

            Assert.Equal(GameConnectionAuthenticationStatus.Authenticated, authentication.Status);
            Assert.Equal(1, store.RedemptionCount);
            Assert.Equal(1, limiter.BeginCount);

            return new AuthenticatedFixture(transport, client, connection, authenticationBoundary);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static GameConnectionHandoffProcessor CreateHandoffProcessor(FakeRedemptionStore store, FakeAttemptLimiter limiter)
    {
        GameLoginTicketRedeemer redeemer = new(store, limiter);
        GameConnectionAuthenticator authenticator = new(redeemer);
        return new GameConnectionHandoffProcessor(new GameTransportHandshakeProcessor(), authenticator);
    }

    private static CharacterLoginProfile CreateProfile(byte rebirthCount = 0, byte preRebirthLevel = 0)
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, "Bernie");
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount, preRebirthLevel);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25, titleId: 321, enlightenmentPoints: 1234, location);
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

    private static ushort[] ReadPacketIds(ReadOnlySpan<byte> plaintext)
    {
        List<ushort> packetIds = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header));

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);
            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

            packetIds.Add(header.PacketId);
            offset += wireLength;
        }

        Assert.Equal(plaintext.Length, offset);
        return packetIds.ToArray();
    }

    private sealed class AuthenticatedFixture(FakeGameTransportConnection transport, GameClientTestPeer client, AuthenticatedGameConnection connection,
        int authenticationBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public AuthenticatedGameConnection Connection { get; } = connection;
        public int AuthenticationBoundary { get; } = authenticationBoundary;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await Connection.DisposeAsync();
            }
        }
    }

    private sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
    {
        public GameLoginTicketIdentity? Result { get; init; }
        public int RedemptionCount { get; private set; }

        public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RedemptionCount++;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakeAttemptLimiter : IGameLoginTicketRedemptionAttemptLimiter
    {
        public int BeginCount { get; private set; }

        public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attempt)
        {
            BeginCount++;
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
