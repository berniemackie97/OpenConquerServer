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
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterEnterMapProcessorTests
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
    public async Task ProcessAsync_ValidEnterMapRequest_WritesVerifiedSequenceAndTransfersConnectionExactlyOnce()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);

        fixture.QueueAction();

        EnteredMapConnection result = await processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken);
        AuthenticatedGameConnection transferredConnection = result.TakeConnection();

        Assert.Same(fixture.Connection, transferredConnection);
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(map, result.Map);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingEnterMap.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());

        byte[][] packets = ReadPackets(fixture.DecryptEnterMapResponse());

        Assert.Equal(3, packets.Length);
        Assert.Equal(GameMapInfoPacket1110.PacketIdentifier, ReadPacketId(packets[0]));
        Assert.Equal(GameWeatherUpdatePacket1016.PacketIdentifier, ReadPacketId(packets[1]));
        Assert.Equal(GameAction10010.PacketIdentifier, ReadPacketId(packets[2]));

        Assert.Equal(20, packets[0].Length);
        Assert.Equal(MapId, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(4)));
        Assert.Equal(MapDataId, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(8)));
        Assert.Equal(unchecked((uint)MapFlags), BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(12)));
        Assert.Equal((uint)(MapFlags >> 32), BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(16)));

        Assert.Equal(20, packets[1].Length);
        Assert.Equal(GameWeatherUpdatePacket1016.ClearKind, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(16)));

        Assert.Equal(GameAction10010.FixedPacketLength, packets[2].Length);
        Assert.Equal(fixture.Profile.Identity.CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(4)));
        Assert.Equal(MapDataId, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(12)));
        Assert.Equal(ServerTick, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(16)));
        Assert.Equal(GameAction10010.EnterMapAction, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(22)));
        Assert.Equal(fixture.Profile.Location.X, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(24)));
        Assert.Equal(fixture.Profile.Location.Y, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(32)));
        Assert.Equal((byte)0, packets[2][36]);
        Assert.Equal((byte)0, packets[2][37]);

        await result.DisposeAsync();

        Assert.Equal(0, fixture.Transport.DisposeCount);

        await transferredConnection.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_MapMetadataDoesNotMatchPersistedMap_RejectsBeforeReadingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        GameMapEntryDefinition map = new(MapId + 1, MapDataId, MapFlags);
        int receiveCallCountBeforeProcessing = fixture.Transport.ReceiveCallCount;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("map", exception.ParamName);
        Assert.Equal(receiveCallCountBeforeProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingEnterMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeEnterMap_DisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingEnterMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutWritingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));

        byte[] packet = BuildActionPacket(fixture.Profile.Identity.CharacterId, GameAction10010.EnterMapAction);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutWritingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));

        fixture.QueueAction(action: GameAction10010.EnterMapAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected EnterMap action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutWritingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));

        fixture.QueueAction(entityId: fixture.Profile.Identity.CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_EnterMapRequestContainsTrailingStrings_RejectsWithoutWritingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        byte[] packet = BuildActionPacket(fixture.Profile.Identity.CharacterId, GameAction10010.EnterMapAction, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_WritesNothingAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), cancellation.Token).AsTask());

        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingEnterMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResponseWrite_WritesNoPartialPacketAndDisposesConnection()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueAction();
        fixture.Transport.BlockSends();

        Task<EnteredMapConnection> processing = processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId, MapDataId, MapFlags), cancellation.Token).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        fixture.Transport.ReleaseSends();

        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingEnterMap.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfAwaitingStateIsRejectedWithoutSecondReadOrWrite()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);

        fixture.QueueAction();

        await using EnteredMapConnection result = await processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken);

        int sentLengthAfterFirstProcessing = fixture.Transport.SentBytes.Length;
        int receiveCallCountAfterFirstProcessing = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLengthAfterFirstProcessing, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCallCountAfterFirstProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfAwaitingStateAllowsExactlyOneOwner()
    {
        await using EnterMapFixture fixture = await CreateFixtureAsync();
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));
        GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);

        fixture.QueueAction();
        fixture.Transport.BlockSends();

        Task<EnteredMapConnection> first = processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync(fixture.AwaitingEnterMap, map, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        await using EnteredMapConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Equal(3, ReadPackets(fixture.DecryptEnterMapResponse()).Length);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using EnterMapFixture fixture = await CreateFixtureAsync(cleanupFailure);
        ExistingCharacterEnterMapProcessor processor = new(new FixedGameTickSource(ServerTick));

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(fixture.AwaitingEnterMap, new GameMapEntryDefinition(MapId + 1, MapDataId, MapFlags), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<ArgumentException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(fixture.EnterMapBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    private static async Task<EnterMapFixture> CreateFixtureAsync(Exception? disposeFailure = null)
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

            return new EnterMapFixture(transport, client, connection, profile, awaitingEnterMap, transport.SentBytes.Length);
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

    private static byte[] BuildActionPacket(uint entityId, ushort action, int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
    {
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameAction10010.PacketIdentifier);

        if (length < GameAction10010.FixedPacketLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), 0x01020304);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), action);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), 0);
        packet[36] = 0;
        packet[37] = stringCount;

        return packet;
    }

    private static ushort ReadPacketId(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2));

    private static byte[][] ReadPackets(ReadOnlySpan<byte> plaintext)
    {
        List<byte[]> packets = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header));

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);
            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

            packets.Add(plaintext.Slice(offset, header.Length).ToArray());
            offset += wireLength;
        }

        Assert.Equal(plaintext.Length, offset);
        return packets.ToArray();
    }

    private sealed class EnterMapFixture(FakeGameTransportConnection transport, GameClientTestPeer client, AuthenticatedGameConnection connection,
        CharacterLoginProfile profile, AwaitingEnterMapConnection awaitingEnterMap, int enterMapBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public AuthenticatedGameConnection Connection { get; } = connection;
        public CharacterLoginProfile Profile { get; } = profile;
        public AwaitingEnterMapConnection AwaitingEnterMap { get; } = awaitingEnterMap;
        public int EnterMapBoundary { get; } = enterMapBoundary;

        public void QueueAction(uint? entityId = null, ushort action = GameAction10010.EnterMapAction)
        {
            QueuePacket(BuildActionPacket(entityId ?? Profile.Identity.CharacterId, action));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

        public byte[] DecryptEnterMapResponse()
        {
            return Client.DecryptServerBytes(Transport.SentBytes[EnterMapBoundary..]);
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
