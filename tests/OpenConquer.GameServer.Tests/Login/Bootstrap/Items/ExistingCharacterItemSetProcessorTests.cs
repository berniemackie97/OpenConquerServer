using System.Buffers.Binary;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Items;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Items;

public sealed class ExistingCharacterItemSetProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint ItemTypeId = 100_000;
    private const uint RequestTimestamp = 0x11223344;

    private static readonly DateTimeOffset s_utcNow = new(2026, 9, 28, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterItemSetProcessor(resolver, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesItemsEquipmentAndAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterItemSet itemSet = new(CharacterId,
        [
            CreateItem(20),
            CreateItem(10, ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.Headwear))),
        ]);
        FakeItemSetResolver resolver = new(itemSet);

        harness.QueueAction(GameAction.GetItemSetAction, timestamp: RequestTimestamp);

        await CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal([10u, 20u], login.ItemSet!.Items.Select(static item => item.ItemId));
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(CharacterId, resolver.CharacterId);
        Assert.Equal(s_utcNow, resolver.UtcNow);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(4, packets.Length);
        Assert.Equal(GameLocalItemSnapshotPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[0]));
        Assert.Equal(GameLocalItemSnapshotPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[1]));
        Assert.Equal(GameActiveEquipmentSnapshotPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[2]));
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[3]));
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(4)));
        Assert.Equal((byte)1, packets[0][18]);
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(4)));
        Assert.Equal(GameLocalItemSnapshotPacket.InventoryPlacement, packets[1][18]);
        Assert.Equal(GameActiveEquipmentSnapshotPacket.MainEquipmentMode, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(8)));
        Assert.Equal(GameActiveEquipmentSnapshotPacket.ActiveEquipmentSnapshotSubtype, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(12)));
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(32)));
        Assert.Equal(GameAction.FixedPacketLength, packets[3].Length);
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(4)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(packets[3].AsSpan(16)));
        Assert.Equal(GameAction.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(packets[3].AsSpan(20)));
    }

    [Fact]
    public async Task ProcessAsync_EmptyItemSet_WritesOnlyAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        harness.QueueAction(GameAction.GetItemSetAction, timestamp: RequestTimestamp);

        await CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(acknowledgement));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(4)));
        Assert.Equal(RequestTimestamp, BinaryPrimitives.ReadUInt32LittleEndian(acknowledgement.AsSpan(16)));
        Assert.Equal(GameAction.GetItemSetAction, BinaryPrimitives.ReadUInt16LittleEndian(acknowledgement.AsSpan(20)));
        Assert.Empty(login.ItemSet!.Items);
        Assert.Equal(1, resolver.ResolveCount);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutResolution()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutResolutionOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetItemSetAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutResolutionOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetItemSetAction, CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutResolutionOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        harness.QueueAction(GameAction.GetItemSetAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected item-set request action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutResolutionOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));

        harness.QueueAction(GameAction.GetItemSetAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutResolutionOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetItemSetAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
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
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetItemSetAction, CharacterId, timestamp: RequestTimestamp);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_ResolverFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException failure = new("item resolution failed");
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, [])) { Failure = failure };

        harness.QueueAction(GameAction.GetItemSetAction);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_ResolverReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        uint otherCharacterId = CharacterId + 1;
        FakeItemSetResolver resolver = new(new CharacterItemSet(otherCharacterId, []));

        harness.QueueAction(GameAction.GetItemSetAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateProcessor(resolver).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(otherCharacterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(CharacterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.ItemSet);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadResolveOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor(resolver).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCount, harness.Transport.ReceiveCallCount);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotResolveOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = CreateProcessor(resolver).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.ReceiveStarted.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResolution_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        BlockingItemSetResolver resolver = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetItemSetAction);

        Task processing = CreateProcessor(resolver).ProcessAsync(login, cancellation.Token).AsTask();

        await resolver.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(CharacterId, resolver.CharacterId);
        Assert.Equal(s_utcNow, resolver.UtcNow);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterResolution_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []), cancellation);

        harness.QueueAction(GameAction.GetItemSetAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor(resolver).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.ItemSet);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringTimestampCapture_StopsBeforeResolution()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, []));
        ExistingCharacterItemSetProcessor processor = new(resolver, new CancelingTimeProvider(s_utcNow, cancellation));

        harness.QueueAction(GameAction.GetItemSetAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeItemSetResolver resolver = new(new CharacterItemSet(CharacterId, [CreateItem(1)]));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetItemSetAction);
        harness.Transport.BlockSends();

        Task processing = CreateProcessor(resolver).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.ItemSet);
    }

    private static ExistingCharacterItemSetProcessor CreateProcessor(ICharacterItemSetResolver resolver) =>
        new(resolver, new LoginStepHarness.FixedTimeProvider(s_utcNow));

    private static CharacterItem CreateItem(uint itemId, ItemPlacement? placement = null)
    {
        return new CharacterItem(itemId, CharacterId, ItemTypeId, placement ?? ItemPlacement.CreateInventory(), 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, 0, null, 0, 0, 0, 1, ItemLifetime.CreatePermanent());
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

    private sealed class CancelingTimeProvider(DateTimeOffset utcNow, CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return utcNow;
        }
    }
}
