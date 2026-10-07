using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin.Redemption;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Character.Bootstrap;
using OpenConquer.GameServer.Login.Character.Resolution;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterItemSetProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const int ResourceVersion = 5517;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;
    private const uint ServerTick = 0xA1B2C3D4;
    private const uint ItemTypeId = 100_000;
    private const uint RequestTimestamp = 0x11223344;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");
    private static readonly DateTimeOffset s_utcNow = new(2026, 9, 28, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterIdentityPolicy.FirstPlayerEntityId, []));

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(resolver, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesItemSetSequenceAndTransfersConnectionExactlyOnce()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        CharacterItemSet itemSet = new(fixture.Profile.Identity.CharacterId,
        [
            CreateItem(fixture.Profile.Identity.CharacterId, 20),
            CreateItem(fixture.Profile.Identity.CharacterId, 10, ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.Headwear))),
        ]);
        FakeItemSetResolver resolver = new(itemSet);
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);

        fixture.QueueItemSetRequest(timestamp: RequestTimestamp);

        AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);
        ExistingCharacterGameConnection transferredConnection = result.TakeConnection();

        Assert.Same(fixture.Profile, transferredConnection.Profile);
        Assert.True(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Equal([10u, 20u], result.ItemSet.Items.Select(static item => item.ItemId));
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(fixture.Profile.Identity.CharacterId, resolver.CharacterId);
        Assert.Equal(s_utcNow, resolver.UtcNow);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingItemSet.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());

        byte[][] packets = ReadPackets(fixture.DecryptItemSetResponse());

        Assert.Equal(4, packets.Length);
        Assert.Equal(GameLocalItemSnapshotPacket1008.PacketIdentifier, ReadPacketId(packets[0]));
        Assert.Equal(GameLocalItemSnapshotPacket1008.PacketIdentifier, ReadPacketId(packets[1]));
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.PacketIdentifier, ReadPacketId(packets[2]));
        Assert.Equal(GameAction10010.PacketIdentifier, ReadPacketId(packets[3]));
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(4)));
        Assert.Equal((byte)1, packets[0][18]);
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(4)));
        Assert.Equal(GameLocalItemSnapshotPacket1008.InventoryPlacement, packets[1][18]);
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(8)));
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.ActiveEquipmentSnapshotSubtype, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(12)));
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(32)));
        Assert.Equal(GameAction10010.FixedPacketLength, packets[3].Length);
        Assert.Equal(fixture.Profile.Identity.CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(4)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(16)));
        Assert.Equal(GameAction10010.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(20)));

        await result.DisposeAsync();
        Assert.Equal(0, fixture.Transport.DisposeCount);

        await transferredConnection.DisposeAsync();
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_EmptyItemSet_WritesOnlyAcknowledgement()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);

        fixture.QueueItemSetRequest(timestamp: RequestTimestamp);

        await using AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);
        byte[] acknowledgement = Assert.Single(ReadPackets(fixture.DecryptItemSetResponse()));

        Assert.Equal(GameAction10010.PacketIdentifier, ReadPacketId(acknowledgement));
        Assert.Equal(fixture.Profile.Identity.CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(4)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(16)));
        Assert.Equal(GameAction10010.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(acknowledgement.AsSpan(20)));
        Assert.Empty(result.ItemSet.Items);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_DisposesConnectionWithoutResolution()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueuePacket(BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest(action: GameAction10010.GetItemSetAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected item-set action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest(entityId: fixture.Profile.Identity.CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, length: 41, stringCount: 1);
        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Theory]
    [InlineData(8, 4)]
    [InlineData(12, 4)]
    [InlineData(22, 2)]
    [InlineData(24, 2)]
    [InlineData(26, 2)]
    [InlineData(28, 4)]
    [InlineData(32, 4)]
    [InlineData(36, 1)]
    public async Task ProcessAsync_NonZeroReservedRequestField_RejectsWithoutResolutionOrWrite(int offset, int width)
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, timestamp: RequestTimestamp);
        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ResolverFailure_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        IOException failure = new("item resolution failed");
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [])) { Failure = failure };
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest();

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_ResolverReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        uint otherCharacterId = fixture.Profile.Identity.CharacterId + 1;
        FakeItemSetResolver resolver = new(new CharacterItemSet(otherCharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(otherCharacterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Profile.Identity.CharacterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadResolveOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCount = fixture.Transport.ReceiveCallCount;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCount, fixture.Transport.ReceiveCallCount);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotResolveOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingFriendListConnection> processing = processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask();
        await fixture.Transport.ReceiveStarted.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResolution_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        BlockingItemSetResolver resolver = new();
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.QueueItemSetRequest();

        Task<AwaitingFriendListConnection> processing = processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask();
        await resolver.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(fixture.Profile.Identity.CharacterId, resolver.CharacterId);
        Assert.Equal(s_utcNow, resolver.UtcNow);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterResolution_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []), cancellation);
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringTimestampCapture_StopsBeforeResolution()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver, new CancelingTimeProvider(s_utcNow, cancellation));
        fixture.QueueItemSetRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_WritesNoPartialPacket()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [CreateItem(fixture.Profile.Identity.CharacterId, 1)]));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.QueueItemSetRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingFriendListConnection> processing = processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask();
        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        fixture.Transport.ReleaseSends();

        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuse_IsRejectedWithoutSecondReadResolutionOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest();

        await using AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);

        int sentLength = fixture.Transport.SentBytes.Length;
        int receiveCallCount = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLength, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCallCount, fixture.Transport.ReceiveCallCount);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuse_AllowsExactlyOneOwner()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [CreateItem(fixture.Profile.Identity.CharacterId, 1)]));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingFriendListConnection> first = processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask();
        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(1, resolver.ResolveCount);
            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        await using AwaitingFriendListConnection result = await first;

        Assert.Single(result.ItemSet.Items);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(2, ReadPackets(fixture.DecryptItemSetResponse()).Length);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingAndCleanupFailure_AreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using ItemSetFixture fixture = await CreateFixtureAsync(cleanupFailure);
        FakeItemSetResolver resolver = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(resolver);
        fixture.QueueItemSetRequest(action: GameAction10010.GetItemSetAction + 1);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
            () => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    private static ExistingCharacterItemSetProcessor CreateProcessor(ICharacterItemSetResolver resolver, TimeProvider? timeProvider = null)
    {
        return new ExistingCharacterItemSetProcessor(resolver, timeProvider ?? new FixedTimeProvider(s_utcNow));
    }

    private static async Task<ItemSetFixture> CreateFixtureAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        GameConnectionHandoffProcessor handoffProcessor = CreateHandoffProcessor(new FakeRedemptionStore
        {
            Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid),
        }, new FakeAttemptLimiter());

        Task<GameConnectionAuthenticationResult> authenticationTask = handoffProcessor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();
        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        int authenticationBoundary = transport.SentBytes.Length;

        try
        {
            transport.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. client.EncryptClientFrame(BuildLoginProof())]);

            GameConnectionAuthenticationResult authentication = await authenticationTask;
            CharacterLoginProfile profile = CreateProfile();
            CharacterLoginHandoffResult handoff = new(authentication.TakeConnection(), CharacterLoginResolution.ExistingCharacter(profile));
            CharacterPresenceDirectory presence = new();
            AwaitingEnterMapConnection awaitingEnterMap = await new ExistingCharacterBootstrapProcessor(presence)
                .ProcessAsync(handoff, TestContext.Current.CancellationToken);

            _ = client.DecryptServerBytes(transport.SentBytes[authenticationBoundary..]);

            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            int enterMapBoundary = transport.SentBytes.Length;

            transport.QueueReceive(client.EncryptClientFrame(BuildActionPacket(GameAction10010.EnterMapAction, profile.Identity.CharacterId, timestamp: 0x01020304)));

            EnteredMapConnection enteredMap = await new ExistingCharacterEnterMapProcessor(new FixedGameTickSource(ServerTick))
                .ProcessAsync(awaitingEnterMap, map, TestContext.Current.CancellationToken);

            _ = client.DecryptServerBytes(transport.SentBytes[enterMapBoundary..]);
            transport.QueueReceive(client.EncryptClientFrame(BuildActionPacket(GameAction10010.ClientStateAppliedAction)));

            AwaitingItemSetConnection awaitingItemSet = await new ExistingCharacterMapStateAppliedProcessor()
                .ProcessAsync(enteredMap, TestContext.Current.CancellationToken);

            return new ItemSetFixture(transport, client, presence, profile, map, awaitingItemSet, transport.SentBytes.Length);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static GameConnectionHandoffProcessor CreateHandoffProcessor(FakeRedemptionStore store, FakeAttemptLimiter limiter)
    {
        return new GameConnectionHandoffProcessor(new GameTransportHandshakeProcessor(), new GameConnectionAuthenticator(new GameLoginTicketRedeemer(store, limiter)));
    }

    private static CharacterLoginProfile CreateProfile()
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, Username),
            new CharacterAppearance(2011003, 339),
            new CharacterProgression(120, 123456789, 60, 10, 20, 2, 130),
            new CharacterAttributes(101, 102, 103, 104, 105),
            new CharacterVitals(1234, 567),
            new CharacterEconomy(1_234_567, 2_345, 678),
            -25, 321, 1234, new CharacterLocation(MapId, 430, 378));
    }

    private static CharacterItem CreateItem(uint characterId, uint itemId, ItemPlacement? placement = null)
    {
        return new CharacterItem(itemId, characterId, ItemTypeId, placement ?? ItemPlacement.CreateInventory(), 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, 0, null, 0, 0, 0, 1, ItemLifetime.CreatePermanent());
    }

    private static byte[] BuildLoginProof()
    {
        byte[] packet = new byte[GameLoginProof1052.PacketLength];
        WireFrameHeader.Write(packet, GameLoginProof1052.PacketLength, GameLoginProof1052.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), SessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), AuthenticationKey);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), GameLoginProof1052.ExpectedMode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), LocaleTag);
        packet[16] = 0x11; packet[17] = 0x22; packet[18] = 0x33; packet[19] = 0x44; packet[20] = 0x55; packet[21] = 0x66;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24), ResourceVersion);
        return packet;
    }

    private static byte[] BuildActionPacket(ushort action, uint entityId = 0, uint parameterPair = 0, uint actionParameter = 0,
        uint timestamp = 0, ushort direction = 0, ushort positionX = 0, ushort positionY = 0, uint data1 = 0, uint data2 = 0,
        byte flag = 0, int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
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

    private static ushort ReadPacketId(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2));

    private static byte[][] ReadPackets(ReadOnlySpan<byte> plaintext)
    {
        List<byte[]> packets = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header));
            Assert.InRange(header.Length, WireFrameHeader.Size, GameWireProtocol.MaximumPacketLength);

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);
            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

            packets.Add(plaintext.Slice(offset, header.Length).ToArray());
            offset += wireLength;
        }

        return packets.ToArray();
    }

    private static void WriteNonZeroValue(Span<byte> packet, int offset, int width)
    {
        switch (width)
        {
            case 1: packet[offset] = 1; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(packet[offset..], 1); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(packet[offset..], 1); break;
            default: throw new ArgumentOutOfRangeException(nameof(width));
        }
    }

    private sealed class ItemSetFixture(FakeGameTransportConnection transport, GameClientTestPeer client, CharacterPresenceDirectory presence,
        CharacterLoginProfile profile, GameMapEntryDefinition map, AwaitingItemSetConnection awaitingItemSet, int itemSetBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public CharacterPresenceDirectory Presence { get; } = presence;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public AwaitingItemSetConnection AwaitingItemSet { get; } = awaitingItemSet;
        public int ItemSetBoundary { get; } = itemSetBoundary;

        public void QueueItemSetRequest(ushort action = GameAction10010.GetItemSetAction, uint? entityId = null, uint timestamp = 0)
            => QueuePacket(BuildActionPacket(action, entityId ?? Profile.Identity.CharacterId, timestamp: timestamp));

        public void QueuePacket(byte[] packet) => Transport.QueueReceive(Client.EncryptClientFrame(packet));
        public byte[] DecryptItemSetResponse() => Client.DecryptServerBytes(Transport.SentBytes[ItemSetBoundary..]);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await AwaitingItemSet.DisposeAsync();
            }
        }
    }

    private sealed class FakeItemSetResolver(CharacterItemSet result) : ICharacterItemSetResolver
    {
        private int _resolveCount;

        public Exception? Failure { get; init; }
        public int ResolveCount => Volatile.Read(ref _resolveCount);
        public uint? CharacterId { get; private set; }
        public DateTimeOffset? UtcNow { get; private set; }

        public ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _resolveCount);
            CharacterId = characterId;
            UtcNow = utcNow;

            return Failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<CharacterItemSet>(Failure);
        }
    }

    private sealed class BlockingItemSetResolver : ICharacterItemSetResolver
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }
        public DateTimeOffset? UtcNow { get; private set; }

        public async ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            UtcNow = utcNow;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingItemSetResolver(CharacterItemSet result, CancellationTokenSource cancellation) : ICharacterItemSetResolver
    {
        private int _resolveCount;

        public int ResolveCount => Volatile.Read(ref _resolveCount);

        public ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _resolveCount);
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CancelingTimeProvider(DateTimeOffset utcNow, CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return utcNow;
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
