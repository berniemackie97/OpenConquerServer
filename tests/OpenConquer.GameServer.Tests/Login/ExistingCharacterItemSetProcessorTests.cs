using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Application.Accounts.GameLogin.Redemption;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login;
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
    private const int SeedTableLength = 128;
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
    private const uint DefaultItemTypeId = 100_000;
    private const uint RequestTimestamp = 0x11223344;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");
    private static readonly DateTimeOffset s_utcNow = new(2026, 9, 28, 21, 0, 0, TimeSpan.Zero);
    private static readonly Encoding s_retailEncoding = CreateRetailEncoding();

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeItemSetRepository repository = new(new CharacterItemSet(CharacterIdentityPolicy.FirstPlayerEntityId, []));
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(null!, itemTypes, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(repository, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(repository, itemTypes, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesItemSetSequenceAndTransfersConnectionExactlyOnce()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        CharacterItemSet itemSet = new(fixture.Profile.Identity.CharacterId,
        [
            CreateItem(fixture.Profile.Identity.CharacterId, 20),
            CreateItem(fixture.Profile.Identity.CharacterId, 10, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Headwear)),
        ]);
        FakeItemSetRepository repository = new(itemSet);
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest(timestamp: RequestTimestamp);

        AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);
        ExistingCharacterGameConnection transferredConnection = result.TakeConnection();

        Assert.Same(fixture.Profile, transferredConnection.Profile);
        Assert.True(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Equal([10u, 20u], result.ItemSet.Items.Select(static item => item.ItemId));
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.Profile.Identity.CharacterId, repository.LastCharacterId);
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
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(12)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(16)));
        Assert.Equal(GameAction10010.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(22)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(32)));
        Assert.Equal((byte)0, packets[3][36]);
        Assert.Equal((byte)0, packets[3][37]);

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
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest(timestamp: RequestTimestamp);

        await using AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);

        byte[][] packets = ReadPackets(fixture.DecryptItemSetResponse());

        byte[] acknowledgement = Assert.Single(packets);
        Assert.Equal(GameAction10010.PacketIdentifier, ReadPacketId(acknowledgement));
        Assert.Equal(fixture.Profile.Identity.CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(4)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(16)));
        Assert.Equal(GameAction10010.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(acknowledgement.AsSpan(20)));
        Assert.Empty(result.ItemSet.Items);
        Assert.True(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeItemSetRequest_DisposesConnectionWithoutRepositoryLoad()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingItemSet.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueuePacket(BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest(action: GameAction10010.GetItemSetAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected item-set action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest(entityId: fixture.Profile.Identity.CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ItemSetRequestContainsTrailingStrings_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
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
    public async Task ProcessAsync_NonZeroReservedRequestField_RejectsWithoutRepositoryLoadOrWrite(int offset, int width)
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetItemSetAction, fixture.Profile.Identity.CharacterId, timestamp: RequestTimestamp);

        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        IOException repositoryFailure = new("item repository failed");
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [])) { Failure = repositoryFailure };
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest();

        IOException exception = await Assert.ThrowsAsync<IOException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(repositoryFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.Profile.Identity.CharacterId, repository.LastCharacterId);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        uint otherCharacterId = fixture.Profile.Identity.CharacterId + 1;
        FakeItemSetRepository repository = new(new CharacterItemSet(otherCharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(otherCharacterId.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Profile.Identity.CharacterId.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.Profile.Identity.CharacterId, repository.LastCharacterId);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ProjectionFailure_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        const uint unknownItemTypeId = 999_999;
        CharacterItem item = CreateItem(fixture.Profile.Identity.CharacterId, 1, itemTypeId: unknownItemTypeId);
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [item]));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository, CreateItemTypeTable((DefaultItemTypeId, 0)));

        fixture.QueueItemSetRequest();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("unknown item type", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWriteAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCountBeforeProcessing = fixture.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCountBeforeProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingItemSet.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotLoadOrWriteAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingFriendListConnection> processing = processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask();

        await fixture.Transport.ReceiveStarted.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRepositoryLoad_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        BlockingItemSetRepository repository = new();
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueItemSetRequest();

        Task<AwaitingFriendListConnection> processing = processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask();

        await repository.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(fixture.Profile.Identity.CharacterId, repository.CharacterId);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterRepositoryReturn_WritesNothingAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []), cancellation);
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.Profile.Identity.CharacterId, repository.CharacterId);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringProjectionTimestampCapture_StopsBeforeFirstWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CharacterItem item = CreateItem(fixture.Profile.Identity.CharacterId, 1);
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [item]));
        ExistingCharacterItemSetProcessor processor = new(repository, CreateItemTypeTable((DefaultItemTypeId, 0)), new CancelingTimeProvider(s_utcNow, cancellation));

        fixture.QueueItemSetRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_WritesNoPartialPacketAndDisposesConnection()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        CharacterItem item = CreateItem(fixture.Profile.Identity.CharacterId, 1);
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [item]));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);
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
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingItemSet.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfAwaitingStateIsRejectedWithoutSecondReadLoadOrWrite()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest();

        await using AwaitingFriendListConnection result = await processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken);

        int sentLengthAfterFirstProcessing = fixture.Transport.SentBytes.Length;
        int receiveCallCountAfterFirstProcessing = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLengthAfterFirstProcessing, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCallCountAfterFirstProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfAwaitingStateAllowsExactlyOneOwner()
    {
        await using ItemSetFixture fixture = await CreateFixtureAsync();
        CharacterItem item = CreateItem(fixture.Profile.Identity.CharacterId, 1);
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, [item]));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingFriendListConnection> first = processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(1, repository.LoadCount);
            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        await using AwaitingFriendListConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Single(result.ItemSet.Items);
        Assert.True(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Equal(2, ReadPackets(fixture.DecryptItemSetResponse()).Length);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using ItemSetFixture fixture = await CreateFixtureAsync(cleanupFailure);
        FakeItemSetRepository repository = new(new CharacterItemSet(fixture.Profile.Identity.CharacterId, []));
        ExistingCharacterItemSetProcessor processor = CreateProcessor(repository);

        fixture.QueueItemSetRequest(action: GameAction10010.GetItemSetAction + 1);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(fixture.AwaitingItemSet, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ItemSetBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(fixture.Profile.Identity.CharacterId));
    }

    private static ExistingCharacterItemSetProcessor CreateProcessor(ICharacterItemSetRepository repository, ItemTypeDatTable? itemTypes = null)
    {
        return new ExistingCharacterItemSetProcessor(repository, itemTypes ?? CreateItemTypeTable((DefaultItemTypeId, 0)), new FixedTimeProvider(s_utcNow));
    }

    private static async Task<ItemSetFixture> CreateFixtureAsync(Exception? disposeFailure = null)
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
            CharacterPresenceDirectory presence = new();
            AwaitingEnterMapConnection awaitingEnterMap = await new ExistingCharacterBootstrapProcessor(presence).ProcessAsync(handoff, TestContext.Current.CancellationToken);

            _ = client.DecryptServerBytes(transport.SentBytes[authenticationBoundary..]);

            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            int enterMapBoundary = transport.SentBytes.Length;

            transport.QueueReceive(client.EncryptClientFrame(BuildActionPacket(GameAction10010.EnterMapAction, profile.Identity.CharacterId, timestamp: 0x01020304)));

            EnteredMapConnection enteredMap = await new ExistingCharacterEnterMapProcessor(new FixedGameTickSource(ServerTick)).ProcessAsync(awaitingEnterMap, map, TestContext.Current.CancellationToken);

            _ = client.DecryptServerBytes(transport.SentBytes[enterMapBoundary..]);

            transport.QueueReceive(client.EncryptClientFrame(BuildActionPacket(GameAction10010.ClientStateAppliedAction)));

            AwaitingItemSetConnection awaitingItemSet = await new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(enteredMap, TestContext.Current.CancellationToken);

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

    private static CharacterItem CreateItem(uint characterId, uint itemId, uint itemTypeId = DefaultItemTypeId, ItemPlacement? placement = null)
    {
        return new CharacterItem(itemId, characterId, itemTypeId, placement ?? ItemPlacement.CreateInventory(),
            durability: 100, maximumDurability: 100, retailCompatibilityByteA: 0,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 0,
            socket1Code: 0, socket2Code: 0, hiddenAttackEffect: 0, retailCompatibilityByteB: 0,
            additionLevel: 0, damageReductionPercentOrSteedCompositionRed: 0, itemBindingCode: 0,
            enchantmentLifeBonusOrSteedCompositionGreen: 0, monsterRestraintIdOrSteedCompositionBlue: 0,
            isSuspicious: false, equipmentLockStateMask: 0, equipmentUnlockAtUtc: null,
            equipmentColor: 0, compositionProgress: 0, inscribedSyndicateId: 0,
            stackQuantity: 1, lifetime: ItemLifetime.CreatePermanent());
    }

    private static ItemPlacement CreateEquipmentPlacement(EquipmentSet set, EquipmentSlot slot)
    {
        return ItemPlacement.CreateEquipment(EquipmentPosition.Create(set, slot));
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

    private static byte[] BuildActionPacket(ushort action, uint entityId = 0, uint parameterPair = 0, uint actionParameter = 0, uint timestamp = 0, ushort direction = 0, ushort positionX = 0, ushort positionY = 0, uint data1 = 0, uint data2 = 0, byte flag = 0, int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
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

    private static ItemTypeDatTable CreateItemTypeTable(params (uint ItemTypeId, int StaticLifetimeMinutes)[] records)
    {
        if (records.Length == 0)
        {
            records = [(DefaultItemTypeId, 0)];
        }

        string decodedText = string.Join("\r\n", records.Select(static record => CreateLine(CreateFields(record.ItemTypeId, record.StaticLifetimeMinutes))));
        return ItemTypeDatTable.Parse(EncodeText(decodedText));
    }

    private static string[] CreateFields(uint itemTypeId, int staticLifetimeMinutes)
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.NativeParsedFieldCount).ToArray();
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = itemTypeId.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.NameFieldIndex] = $"Item{itemTypeId}";
        fields[ItemTypeDatRecord.RequiredLevelFieldIndex] = "0";
        fields[ItemTypeDatRecord.SpeedPercentOffsetFieldIndex] = "0";
        fields[ItemTypeDatRecord.LifeFieldIndex] = "0";
        fields[ItemTypeDatRecord.ManaFieldIndex] = "0";
        fields[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex] = staticLifetimeMinutes.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.StackCapacityFieldIndex] = "1";
        fields[ItemTypeDatRecord.TypeDescriptionFieldIndex] = string.Empty;
        fields[ItemTypeDatRecord.ItemDescriptionFieldIndex] = string.Empty;
        return fields;
    }

    private static string CreateLine(string[] fields) => string.Join("@@", fields) + "@@";

    private static byte[] EncodeText(string decodedText)
    {
        byte[] encodedPayload = s_retailEncoding.GetBytes(decodedText);
        Span<byte> seedTable = stackalloc byte[SeedTableLength];

        BuildSeedTable(seedTable, ItemTypeDatTable.DecodedTextSeed);

        for (int index = 0; index < encodedPayload.Length; index++)
        {
            int rotation = index & 7;
            byte transformed = rotation == 0 ? encodedPayload[index] : RotateLeft(encodedPayload[index], rotation);
            encodedPayload[index] = (byte)(transformed ^ seedTable[index % SeedTableLength]);
        }

        return encodedPayload;
    }

    private static void BuildSeedTable(Span<byte> seedTable, int seed)
    {
        uint state = unchecked((uint)seed);

        for (int index = 0; index < seedTable.Length; index++)
        {
            state = unchecked(state * 214013u + 2531011u);
            seedTable[index] = (byte)(((state >> 16) & 0x7FFFu) % 256u);
        }
    }

    private static byte RotateLeft(byte value, int bitCount) => (byte)((value << bitCount) | (value >> (8 - bitCount)));

    private static Encoding CreateRetailEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(ItemTypeDatTable.RetailCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private sealed class ItemSetFixture(FakeGameTransportConnection transport, GameClientTestPeer client, CharacterPresenceDirectory presence, CharacterLoginProfile profile, GameMapEntryDefinition map, AwaitingItemSetConnection awaitingItemSet, int itemSetBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public CharacterPresenceDirectory Presence { get; } = presence;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public AwaitingItemSetConnection AwaitingItemSet { get; } = awaitingItemSet;
        public int ItemSetBoundary { get; } = itemSetBoundary;

        public void QueueItemSetRequest(ushort action = GameAction10010.GetItemSetAction, uint? entityId = null, uint timestamp = 0)
        {
            QueuePacket(BuildActionPacket(action, entityId ?? Profile.Identity.CharacterId, timestamp: timestamp));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

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

    private sealed class FakeItemSetRepository(CharacterItemSet result) : ICharacterItemSetRepository
    {
        private int _loadCount;

        public Exception? Failure { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? LastCharacterId { get; private set; }

        public ValueTask<CharacterItemSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            LastCharacterId = characterId;

            if (Failure is not null)
            {
                return ValueTask.FromException<CharacterItemSet>(Failure);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingItemSetRepository : ICharacterItemSetRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }

        public async ValueTask<CharacterItemSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingItemSetRepository(CharacterItemSet result, CancellationTokenSource cancellation) : ICharacterItemSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? CharacterId { get; private set; }

        public ValueTask<CharacterItemSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            CharacterId = characterId;
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
