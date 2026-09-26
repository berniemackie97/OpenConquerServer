using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Application.Accounts.GameLogin.Redemption;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character.Bootstrap;
using OpenConquer.GameServer.Login.Character.Resolution;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterMapStateAppliedProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;
    private const uint ServerTick = 0xA1B2C3D4;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");

    [Fact]
    public async Task ProcessAsync_ValidClientStateApplied_TransfersConnectionExactlyOnceWithoutWriting()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueueClientStateApplied();

        AwaitingItemSetConnection result = await processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken);
        AuthenticatedGameConnection transferredConnection = result.TakeConnection();

        Assert.Same(fixture.Connection, transferredConnection);
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.EnteredMap.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());

        await result.DisposeAsync();

        Assert.Equal(0, fixture.Transport.DisposeCount);

        await transferredConnection.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeClientStateApplied_DisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.EnteredMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutWritingAndDisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();
        byte[] packet = BuildActionPacket(GameAction10010.ClientStateAppliedAction);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutWritingAndDisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.ClientStateAppliedAction, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutWritingAndDisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.EnterMapAction));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected client-state-applied action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Theory]
    [InlineData(39, 0)]
    [InlineData(GameAction10010.FixedPacketLength, 1)]
    public async Task ProcessAsync_NonFixedActionBody_RejectsWithoutWritingAndDisposesConnection(int length, byte stringCount)
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.ClientStateAppliedAction, length: length, stringCount: stringCount));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("fixed MsgAction body", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(12, 4)]
    [InlineData(16, 4)]
    [InlineData(22, 2)]
    [InlineData(24, 2)]
    [InlineData(26, 2)]
    [InlineData(28, 4)]
    [InlineData(32, 4)]
    [InlineData(36, 1)]
    public async Task ProcessAsync_NonZeroNativeField_RejectsWithoutWritingAndDisposesConnection(int offset, int width)
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();
        byte[] packet = BuildActionPacket(GameAction10010.ClientStateAppliedAction);

        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("all non-action MsgAction fields to be zero", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_WritesNothingAndDisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, cancellation.Token).AsTask());

        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.EnteredMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingNotification_WritesNothingAndDisposesConnection()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingItemSetConnection> processing = processor.ProcessAsync(fixture.EnteredMap, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.EnteredMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfEnteredMapStateIsRejectedWithoutSecondWrite()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueueClientStateApplied();

        await using AwaitingItemSetConnection result = await processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken);
        int sentLengthAfterFirstProcessing = fixture.Transport.SentBytes.Length;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLengthAfterFirstProcessing, fixture.Transport.SentBytes.Length);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfEnteredMapStateAllowsExactlyOneOwner()
    {
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync();
        ExistingCharacterMapStateAppliedProcessor processor = new();

        Task<AwaitingItemSetConnection> first = processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(0, fixture.Transport.DisposeCount);

        fixture.QueueClientStateApplied();

        await using AwaitingItemSetConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using MapStateAppliedFixture fixture = await CreateFixtureAsync(cleanupFailure);
        ExistingCharacterMapStateAppliedProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.EnterMapAction));

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(fixture.EnteredMap, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(fixture.MapStateAppliedBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    private static async Task<MapStateAppliedFixture> CreateFixtureAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid) };
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor handoffProcessor = CreateHandoffProcessor(store, limiter);
        Task<GameConnectionAuthenticationResult> authenticationTask = handoffProcessor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        int authenticationBoundary = transport.SentBytes.Length;

        try
        {
            transport.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. client.EncryptClientFrame(BuildLoginProof())]);

            GameConnectionAuthenticationResult authentication = await authenticationTask;
            AuthenticatedGameConnection connection = authentication.TakeConnection();
            CharacterLoginProfile profile = CreateProfile();
            CharacterLoginHandoffResult handoff = new(connection, CharacterLoginResolution.ExistingCharacter(profile));
            AwaitingEnterMapConnection awaitingEnterMap = await new ExistingCharacterBootstrapProcessor().ProcessAsync(handoff, TestContext.Current.CancellationToken);

            byte[] encryptedBootstrap = transport.SentBytes[authenticationBoundary..];
            _ = client.DecryptServerBytes(encryptedBootstrap);

            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            int enterMapBoundary = transport.SentBytes.Length;

            transport.QueueReceive(client.EncryptClientFrame(BuildActionPacket(
                GameAction10010.EnterMapAction,
                entityId: profile.Identity.CharacterId,
                timestamp: 0x01020304)));

            EnteredMapConnection enteredMap = await new ExistingCharacterEnterMapProcessor(new FixedGameTickSource(ServerTick))
                .ProcessAsync(awaitingEnterMap, map, TestContext.Current.CancellationToken);

            byte[] encryptedEnterMapResponse = transport.SentBytes[enterMapBoundary..];
            _ = client.DecryptServerBytes(encryptedEnterMapResponse);

            return new MapStateAppliedFixture(transport, client, connection, profile, map, enteredMap, transport.SentBytes.Length);
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

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount: 2, preRebirthLevel: 130);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(MapId, x: 430, y: 378);

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

    private static byte[] BuildActionPacket(
        ushort action,
        uint entityId = 0,
        uint parameterPair = 0,
        uint actionParameter = 0,
        uint timestamp = 0,
        ushort direction = 0,
        ushort positionX = 0,
        ushort positionY = 0,
        uint data1 = 0,
        uint data2 = 0,
        byte flag = 0,
        int length = GameAction10010.FixedPacketLength,
        byte stringCount = 0)
    {
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameAction10010.PacketIdentifier);

        if (length < GameAction10010.FixedPacketLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), parameterPair);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), actionParameter);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), action);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22), direction);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(24), positionX);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26), positionY);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), data1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), data2);
        packet[36] = flag;
        packet[37] = stringCount;

        return packet;
    }

    private static void WriteNonZeroValue(Span<byte> packet, int offset, int width)
    {
        switch (width)
        {
            case 1:
                packet[offset] = 1;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(packet[offset..], 1);
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(packet[offset..], 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(width));
        }
    }

    private sealed class MapStateAppliedFixture(
        FakeGameTransportConnection transport,
        GameClientTestPeer client,
        AuthenticatedGameConnection connection,
        CharacterLoginProfile profile,
        GameMapEntryDefinition map,
        EnteredMapConnection enteredMap,
        int mapStateAppliedBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public AuthenticatedGameConnection Connection { get; } = connection;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public EnteredMapConnection EnteredMap { get; } = enteredMap;
        public int MapStateAppliedBoundary { get; } = mapStateAppliedBoundary;

        public void QueueClientStateApplied()
        {
            QueuePacket(BuildActionPacket(GameAction10010.ClientStateAppliedAction));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await Connection.DisposeAsync();
            }
        }
    }

    private sealed class FixedGameTickSource(uint currentTick) : IGameTickSource
    {
        public uint CurrentTick { get; } = currentTick;
    }

    private sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
    {
        public GameLoginTicketIdentity? Result { get; init; }

        public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakeAttemptLimiter : IGameLoginTicketRedemptionAttemptLimiter
    {
        public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attemptLease)
        {
            attemptLease = new FakeAttemptLease();
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
