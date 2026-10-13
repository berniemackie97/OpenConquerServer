using System.Buffers.Binary;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Syndicates;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Syndicates;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Syndicates;

public sealed class ExistingCharacterSyndicateAttributesProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint RequestTimestamp = 0x11223344;
    private const ushort SyndicateId = 73;

    private static readonly DateTimeOffset s_utcNow = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly uint s_joinDate = checked((uint)new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());
    private static readonly uint s_expirationDate = checked((uint)new DateTimeOffset(2027, 2, 3, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterSyndicateAttributesProcessor(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterSyndicateAttributesProcessor(new FakeSyndicateRepository(CreateEmptyState()), null!));
    }

    [Fact]
    public async Task ProcessAsync_Member_WritesAttributesThenAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterSyndicateState state = CreateMemberState();
        FakeSyndicateRepository repository = new(state);

        harness.QueueAction(GameAction.GetSyndicateAttributesAction, timestamp: RequestTimestamp);

        await CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Same(state, login.SyndicateState);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(2, packets.Length);
        AssertSyndicatePacket(packets[0]);
        AssertAcknowledgement(packets[1], RequestTimestamp);
    }

    [Fact]
    public async Task ProcessAsync_NoMembership_WritesOnlyAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterSyndicateState state = CreateEmptyState();
        FakeSyndicateRepository repository = new(state);

        harness.QueueAction(GameAction.GetSyndicateAttributesAction, timestamp: RequestTimestamp);

        await CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        AssertAcknowledgement(acknowledgement, RequestTimestamp);
        Assert.Same(state, login.SyndicateState);
        Assert.False(login.SyndicateState!.HasMembership);
        Assert.Null(login.SyndicateState.Syndicate);
        Assert.Equal(1, repository.LoadCount);
    }

    [Fact]
    public void Projection_ZeroDatesRemainZero()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: 0, joinDate: 0);

        GameSyndicateAttributesPacket packet = Assert.IsType<GameSyndicateAttributesPacket>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
        Assert.Equal(0u, packet.JoinDate);
    }

    [Fact]
    public void Projection_ExpiredPositionIsZeroButJoinDateIsPreserved()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: s_joinDate);

        GameSyndicateAttributesPacket packet = Assert.IsType<GameSyndicateAttributesPacket>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
        Assert.Equal(20200102u, packet.JoinDate);
    }

    [Fact]
    public void Projection_ExpirationAtCurrentInstantIsExpired()
    {
        uint now = checked((uint)s_utcNow.ToUnixTimeSeconds());
        CharacterSyndicateState state = CreateMemberState(positionExpiration: now);

        GameSyndicateAttributesPacket packet = Assert.IsType<GameSyndicateAttributesPacket>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
    }

    [Fact]
    public void Projection_MaximumUnixDateIsRepresentable()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: uint.MaxValue, joinDate: uint.MaxValue);

        GameSyndicateAttributesPacket packet = Assert.IsType<GameSyndicateAttributesPacket>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(21060207u, packet.PositionExpirationDate);
        Assert.Equal(21060207u, packet.JoinDate);
    }

    [Fact]
    public void Projection_RejectsNonUtcTime()
    {
        CharacterSyndicateState state = CreateMemberState();
        DateTimeOffset localTime = s_utcNow.ToOffset(TimeSpan.FromHours(-4));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, localTime));

        Assert.Equal("utcNow", exception.ParamName);
    }

    [Fact]
    public void Projection_ZeroPopulationFailsClosed()
    {
        CharacterSyndicateState state = CreateMemberState(population: 0);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Contains("population", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedAction_RejectsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());

        harness.QueueAction(GameAction.GetMagicSetAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected syndicate-attributes request action", exception.Message, StringComparison.Ordinal);
        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_EarlySilentInfoReport_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateEmptyState());

        harness.QueueAction(GameAction.ReportSilentInfoAction);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacketId_RejectsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetSyndicateAttributesAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 9999);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedRequest_RejectsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetSyndicateAttributesAction, CharacterId, length: 37));

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetSyndicateAttributesAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());

        harness.QueueAction(GameAction.GetSyndicateAttributesAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        AssertNothingLoadedOrWritten(harness, repository);
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
    public async Task ProcessAsync_NonZeroReservedField_RejectsWithoutHydration(int offset, int width)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetSyndicateAttributesAction, CharacterId);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException failure = new("syndicate repository failed");
        FakeSyndicateRepository repository = new(CreateMemberState()) { Failure = failure };

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateEmptyState(CharacterId + 1));

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated syndicate state belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.SyndicateState);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPersistedPopulation_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState(population: 0));

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCount, harness.Transport.ReceiveCallCount);
        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_StopsWithoutHydration()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = CreateProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        AssertNothingLoadedOrWritten(harness, repository);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRepositoryLoad_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        BlockingSyndicateRepository repository = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);

        Task processing = CreateProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

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
        CancelingSyndicateRepository repository = new(CreateMemberState(), cancellation);

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.SyndicateState);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeSyndicateRepository repository = new(CreateMemberState());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetSyndicateAttributesAction);
        harness.Transport.BlockSends();

        Task processing = CreateProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static ExistingCharacterSyndicateAttributesProcessor CreateProcessor(ICharacterSyndicateStateRepository repository) =>
        new(repository, new LoginStepHarness.FixedTimeProvider(s_utcNow));

    private static CharacterSyndicateState CreateEmptyState(uint characterId = CharacterId) =>
        new(characterId, membership: null, syndicate: null);

    private static CharacterSyndicateState CreateMemberState(uint population = 2, uint? positionExpiration = null, uint? joinDate = null)
    {
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(CharacterId, SyndicateId, uint.MaxValue,
            uint.MaxValue, positionExpiration ?? s_expirationDate, joinDate ?? s_joinDate);

        Syndicate syndicate = new(SyndicateId, "GuildAlpha", CharacterId + 1, "LeaderName", ulong.MaxValue,
            uint.MaxValue, population, 99, 35, 2);

        return new CharacterSyndicateState(CharacterId, membership, syndicate);
    }

    private static void AssertSyndicatePacket(byte[] packet)
    {
        Assert.Equal(GameSyndicateAttributesPacket.FixedPacketLength, packet.Length);
        Assert.Equal(GameSyndicateAttributesPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal((uint)SyndicateId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(ulong.MaxValue, BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(12)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(20)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(24)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28)));

        Assert.True(packet.AsSpan(32, 10).SequenceEqual("LeaderName"u8));
        Assert.All(packet[42..48], value => Assert.Equal((byte)0, value));

        Assert.Equal(99u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(48)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(52)));
        Assert.Equal(35u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(56)));
        Assert.Equal((byte)0, packet[60]);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(61)));
        Assert.Equal(20270203u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(63)));
        Assert.Equal(20200102u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(67)));
        Assert.All(packet[71..75], value => Assert.Equal((byte)0, value));

        Assert.True(packet.AsSpan(75, 10).SequenceEqual("GuildAlpha"u8));
        Assert.All(packet[85..92], value => Assert.Equal((byte)0, value));
    }

    private static void AssertAcknowledgement(byte[] packet, uint timestamp)
    {
        Assert.Equal(GameAction.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction.GetSyndicateAttributesAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.All(packet[22..], value => Assert.Equal((byte)0, value));
    }

    private static void AssertNothingLoadedOrWritten(LoginStepHarness harness, FakeSyndicateRepository repository)
    {
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    private sealed class FakeSyndicateRepository(CharacterSyndicateState result) : ICharacterSyndicateStateRepository
    {
        private int _loadCount;

        public Exception? Failure { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? LastCharacterId { get; private set; }

        public ValueTask<CharacterSyndicateState> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            LastCharacterId = characterId;

            return Failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<CharacterSyndicateState>(Failure);
        }
    }

    private sealed class BlockingSyndicateRepository : ICharacterSyndicateStateRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }

        public async ValueTask<CharacterSyndicateState> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            _started.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingSyndicateRepository(CharacterSyndicateState result, CancellationTokenSource cancellation)
        : ICharacterSyndicateStateRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public ValueTask<CharacterSyndicateState> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            cancellation.Cancel();

            return ValueTask.FromResult(result);
        }
    }
}
