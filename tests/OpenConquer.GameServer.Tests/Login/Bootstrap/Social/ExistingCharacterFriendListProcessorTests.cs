using System.Buffers.Binary;
using System.Text;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Social;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Social;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Social;

public sealed class ExistingCharacterFriendListProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint FriendId = CharacterId + 1;
    private const uint EnemyId = CharacterId + 2;
    private const uint RequestTimestamp = 0x11223344;

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        CharacterPresenceDirectory presence = new();

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterFriendListProcessor(null!, presence));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterFriendListProcessor(repository, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesRelationsThenAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterSocialRelationSet relationSet = new(CharacterId,
        [
            CreateRelation(EnemyId, SocialRelationKind.Enemy, "Rival"),
            CreateRelation(FriendId, SocialRelationKind.Friend, "Ally"),
        ]);
        FakeSocialRepository repository = new(relationSet);
        using ICharacterPresenceLease friendPresence = harness.Presence.Register(FriendId);

        harness.QueueAction(GameAction.GetFriendListAction, timestamp: RequestTimestamp);

        await new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Same(relationSet, login.SocialRelationSet);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(3, packets.Length);
        AssertRelationPacket(packets[0], FriendId, GameSocialRelationAction.AddFriendSilently, stateFlag: 1, "Ally");
        AssertRelationPacket(packets[1], EnemyId, GameSocialRelationAction.AddEnemy, stateFlag: 0, "Rival");
        AssertAcknowledgement(packets[2], RequestTimestamp);
    }

    [Fact]
    public async Task ProcessAsync_EmptyRelationSet_WritesOnlyAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));

        harness.QueueAction(GameAction.GetFriendListAction, timestamp: RequestTimestamp);

        await new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        AssertAcknowledgement(acknowledgement, RequestTimestamp);
        Assert.Empty(login.SocialRelationSet!.Relations);
    }

    [Fact]
    public async Task ProcessAsync_MoreThanFiftyRelations_WritesEveryRelationAndAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterSocialRelation[] relations = Enumerable.Range(1, 51)
            .Select(index => CreateRelation(CharacterId + checked((uint)index), SocialRelationKind.Friend, $"Friend{index}"))
            .ToArray();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, relations));

        harness.QueueAction(GameAction.GetFriendListAction, timestamp: RequestTimestamp);

        await new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(52, packets.Length);
        Assert.All(packets.Take(51), packet => Assert.Equal(GameSocialRelation.PacketIdentifier, LoginStepHarness.ReadPacketId(packet)));
        AssertAcknowledgement(packets[51], RequestTimestamp);
        Assert.Equal(51, login.SocialRelationSet!.Count);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutRepositoryLoad()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetFriendListAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetFriendListAction, CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));

        harness.QueueAction(GameAction.GetFriendListAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected friend-list request action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));

        harness.QueueAction(GameAction.GetFriendListAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetFriendListAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
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
    public async Task ProcessAsync_NonZeroReservedRequestField_RejectsWithoutRepositoryLoadOrWrite(int offset, int width)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetFriendListAction, CharacterId, timestamp: RequestTimestamp);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("requires all non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException repositoryFailure = new("social repository failed");
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, [])) { Failure = repositoryFailure };

        harness.QueueAction(GameAction.GetFriendListAction);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(repositoryFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId + 1, []));

        harness.QueueAction(GameAction.GetFriendListAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated social-relation set belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.SocialRelationSet);
    }

    [Fact]
    public async Task ProcessAsync_ProjectionFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        InvalidOperationException projectionFailure = new("presence lookup failed");
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]));

        harness.QueueAction(GameAction.GetFriendListAction);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ExistingCharacterFriendListProcessor(repository, new ThrowingPresenceReader(projectionFailure)).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(projectionFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCount, harness.Transport.ReceiveCallCount);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRepositoryLoad_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        BlockingSocialRepository repository = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetFriendListAction);

        Task processing = new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, cancellation.Token).AsTask();

        await repository.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterRepositoryReturn_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []), cancellation);

        harness.QueueAction(GameAction.GetFriendListAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.SocialRelationSet);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringProjection_StopsBeforeFirstWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]));

        harness.QueueAction(GameAction.GetFriendListAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterFriendListProcessor(repository, new CancelingPresenceReader(cancellation)).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetFriendListAction);
        harness.Transport.BlockSends();

        Task processing = new ExistingCharacterFriendListProcessor(repository, harness.Presence).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static CharacterSocialRelation CreateRelation(uint counterpartCharacterId, SocialRelationKind kind, string name)
    {
        return new CharacterSocialRelation(SocialRelation.Create(CharacterId, counterpartCharacterId, kind), name);
    }

    private static void AssertRelationPacket(byte[] packet, uint entityId, GameSocialRelationAction action, byte stateFlag, string name)
    {
        Assert.Equal(GameSocialRelation.FixedPacketLength, packet.Length);
        Assert.Equal(GameSocialRelation.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(entityId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal((byte)action, packet[8]);
        Assert.Equal(stateFlag, packet[9]);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));

        byte[] encodedName = Encoding.ASCII.GetBytes(name);

        Assert.True(packet.AsSpan(20, encodedName.Length).SequenceEqual(encodedName));
        Assert.Equal((byte)0, packet[35]);
    }

    private static void AssertAcknowledgement(byte[] packet, uint timestamp)
    {
        Assert.Equal(GameAction.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction.GetFriendListAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(22)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32)));
        Assert.Equal((byte)0, packet[36]);
        Assert.Equal((byte)0, packet[37]);
    }

    private sealed class FakeSocialRepository(CharacterSocialRelationSet result) : ICharacterSocialRelationSetRepository
    {
        private int _loadCount;

        public Exception? Failure { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? LastCharacterId { get; private set; }

        public ValueTask<CharacterSocialRelationSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            LastCharacterId = characterId;

            return Failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<CharacterSocialRelationSet>(Failure);
        }
    }

    private sealed class BlockingSocialRepository : ICharacterSocialRelationSetRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }

        public async ValueTask<CharacterSocialRelationSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingSocialRepository(CharacterSocialRelationSet result, CancellationTokenSource cancellation) : ICharacterSocialRelationSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? CharacterId { get; private set; }

        public ValueTask<CharacterSocialRelationSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            CharacterId = characterId;
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingPresenceReader(Exception failure) : ICharacterPresenceReader
    {
        public bool IsOnline(uint characterId) => throw failure;
    }

    private sealed class CancelingPresenceReader(CancellationTokenSource cancellation) : ICharacterPresenceReader
    {
        public bool IsOnline(uint characterId)
        {
            cancellation.Cancel();
            return false;
        }
    }
}
