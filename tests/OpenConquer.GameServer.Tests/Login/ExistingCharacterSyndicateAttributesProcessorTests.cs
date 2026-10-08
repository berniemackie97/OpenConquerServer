using System.Buffers.Binary;
using System.Net;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterSyndicateAttributesProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;
    private const uint RequestTimestamp = 0x11223344;
    private const ushort SyndicateId = 73;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");
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
    public async Task ProcessAsync_Member_WritesAttributesThenAcknowledgementAndTransfersStateExactlyOnce()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        CharacterSyndicateState state = CreateMemberState();
        FakeSyndicateRepository repository = new(state);
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(timestamp: RequestTimestamp);

        AwaitingSilentInfoReportConnection result = await processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken);

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Same(fixture.ItemSet, result.ItemSet);
        Assert.Same(fixture.SocialRelationSet, result.SocialRelationSet);
        Assert.Same(fixture.WeaponSkillSet, result.WeaponSkillSet);
        Assert.Same(fixture.MagicSet, result.MagicSet);
        Assert.Same(state, result.SyndicateState);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(0, fixture.Transport.DisposeCount);

        byte[][] packets = ReadPackets(fixture.DecryptResponse());

        Assert.Equal(2, packets.Length);
        AssertSyndicatePacket(packets[0]);
        AssertAcknowledgement(packets[1], RequestTimestamp);

        ExistingCharacterGameConnection connection = result.TakeConnection();

        Assert.Same(fixture.Profile, connection.Profile);
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingSyndicateAttributes.TakeConnection());

        await result.DisposeAsync();

        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await connection.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_NoMembership_WritesOnlyAcknowledgement()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        CharacterSyndicateState state = CreateEmptyState();
        FakeSyndicateRepository repository = new(state);
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(timestamp: RequestTimestamp);

        AwaitingSilentInfoReportConnection result = await processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(ReadPackets(fixture.DecryptResponse()));

        AssertAcknowledgement(acknowledgement, RequestTimestamp);
        Assert.Same(state, result.SyndicateState);
        Assert.False(result.SyndicateState.HasMembership);
        Assert.Null(result.SyndicateState.Syndicate);
        Assert.Equal(1, repository.LoadCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public void Projection_ZeroDatesRemainZero()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: 0, joinDate: 0);

        GameSyndicateAttributeInfoPacket1106 packet = Assert.IsType<GameSyndicateAttributeInfoPacket1106>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
        Assert.Equal(0u, packet.JoinDate);
    }

    [Fact]
    public void Projection_ExpiredPositionIsZeroButJoinDateIsPreserved()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: s_joinDate);

        GameSyndicateAttributeInfoPacket1106 packet = Assert.IsType<GameSyndicateAttributeInfoPacket1106>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
        Assert.Equal(20200102u, packet.JoinDate);
    }

    [Fact]
    public void Projection_ExpirationAtCurrentInstantIsExpired()
    {
        uint now = checked((uint)s_utcNow.ToUnixTimeSeconds());
        CharacterSyndicateState state = CreateMemberState(positionExpiration: now);

        GameSyndicateAttributeInfoPacket1106 packet = Assert.IsType<GameSyndicateAttributeInfoPacket1106>(
            ExistingCharacterSyndicateAttributesWireProjection.Create(state, s_utcNow));

        Assert.Equal(0u, packet.PositionExpirationDate);
    }

    [Fact]
    public void Projection_MaximumUnixDateIsRepresentable()
    {
        CharacterSyndicateState state = CreateMemberState(positionExpiration: uint.MaxValue, joinDate: uint.MaxValue);

        GameSyndicateAttributeInfoPacket1106 packet = Assert.IsType<GameSyndicateAttributeInfoPacket1106>(
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
    public async Task ProcessAsync_PeerClosesBeforeRequest_DisposesWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedAction_RejectsWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(action: GameAction10010.GetMagicSetAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected syndicate-attributes action", exception.Message, StringComparison.Ordinal);
        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_EarlySilentInfoReport_IsRejected()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateEmptyState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(action: 0xFB);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacketId_RejectsWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetSyndicateAttributesAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 9999);
        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedRequest_RejectsWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueuePacket(BuildActionPacket(GameAction10010.GetSyndicateAttributesAction, CharacterId, length: 37));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetSyndicateAttributesAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        AssertFailureBeforeWrite(fixture, repository);
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
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        byte[] packet = BuildActionPacket(GameAction10010.GetSyndicateAttributesAction, CharacterId);

        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothingAndDisposes()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        IOException failure = new("syndicate repository failed");
        FakeSyndicateRepository repository = new(CreateMemberState()) { Failure = failure };
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest();

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateEmptyState(CharacterId + 1));
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated syndicate state belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_InvalidPersistedPopulation_FailsBeforeWriting()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState(population: 0));
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWrite()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCount = fixture.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, cancellation.Token).AsTask());

        Assert.Equal(receiveCount, fixture.Transport.ReceiveCallCount);
        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DisposesWithoutHydration()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingSilentInfoReportConnection> processing = processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        AssertFailureBeforeWrite(fixture, repository);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRepositoryLoad_DisposesWithoutWriting()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        BlockingSyndicateRepository repository = new();
        ExistingCharacterSyndicateAttributesProcessor processor = new(repository, CreateTimeProvider());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueSyndicateRequest();

        Task<AwaitingSilentInfoReportConnection> processing = processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, cancellation.Token).AsTask();

        await repository.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterRepositoryReturn_WritesNothing()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingSyndicateRepository repository = new(CreateMemberState(), cancellation);
        ExistingCharacterSyndicateAttributesProcessor processor = new(repository, CreateTimeProvider());

        fixture.QueueSyndicateRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationDuringLoad_DoesNotRemoveReplacement()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        BlockingSyndicateRepository repository = new();
        ExistingCharacterSyndicateAttributesProcessor processor = new(repository, CreateTimeProvider());

        fixture.QueueSyndicateRequest();

        Task<AwaitingSilentInfoReportConnection> processing = processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask();

        await repository.Started.WaitAsync(TestContext.Current.CancellationToken);
        ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

            Assert.True(fixture.Presence.IsOnline(CharacterId));
            Assert.Equal(1, fixture.Transport.DisposeCount);
            Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        }
        finally
        {
            replacement.Dispose();
        }

        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResponseWrite_DisposesWithoutPartialFrame()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateMemberState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueSyndicateRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingSilentInfoReportConnection> processing = processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, cancellation.Token).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        fixture.Transport.ReleaseSends();

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfAwaitingState_IsRejected()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateEmptyState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest();

        AwaitingSilentInfoReportConnection result = await processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken);
        int sentLength = fixture.Transport.SentBytes.Length;
        int receiveCount = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLength, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCount, fixture.Transport.ReceiveCallCount);
        Assert.Equal(1, repository.LoadCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await result.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfAwaitingState_AllowsOneOwner()
    {
        await using SyndicateFixture fixture = await CreateFixtureAsync();
        FakeSyndicateRepository repository = new(CreateEmptyState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingSilentInfoReportConnection> first = processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(1, repository.LoadCount);
            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        AwaitingSilentInfoReportConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(1, repository.LoadCount);
        Assert.Single(ReadPackets(fixture.DecryptResponse()));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ProcessingAndCleanupFailures_AreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using SyndicateFixture fixture = await CreateFixtureAsync(cleanupFailure);
        FakeSyndicateRepository repository = new(CreateEmptyState());
        ExistingCharacterSyndicateAttributesProcessor processor = CreateProcessor(repository);

        fixture.QueueSyndicateRequest(action: GameAction10010.GetMagicSetAction);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(fixture.AwaitingSyndicateAttributes, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        AssertFailureBeforeWrite(fixture, repository);
    }

    private static ExistingCharacterSyndicateAttributesProcessor CreateProcessor(ICharacterSyndicateStateRepository repository) =>
        new(repository, CreateTimeProvider());

    private static TimeProvider CreateTimeProvider() => new FixedTimeProvider(s_utcNow);

    private static CharacterSyndicateState CreateEmptyState(uint characterId = CharacterId) =>
        new(characterId, membership: null, syndicate: null);

    private static CharacterSyndicateState CreateMemberState(uint characterId = CharacterId, uint population = 2,
        uint? positionExpiration = null, uint? joinDate = null)
    {
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(characterId, SyndicateId, uint.MaxValue,
            uint.MaxValue, positionExpiration ?? s_expirationDate, joinDate ?? s_joinDate);

        Syndicate syndicate = new(SyndicateId, "GuildAlpha", CharacterId + 1, "LeaderName", ulong.MaxValue,
            uint.MaxValue, population, 99, 35, 2);

        return new CharacterSyndicateState(characterId, membership, syndicate);
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10,
            previousProfession: 20, rebirthCount: 2, preRebirthLevel: 130);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(MapId, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25,
            titleId: 321, enlightenmentPoints: 1234, location);
    }

    private static async Task<SyndicateFixture> CreateFixtureAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);
        ExistingCharacterGameConnection? connection = null;

        try
        {
            AuthenticatedGameConnection authenticatedConnection = new(AccountId, Username, SessionUid, LocaleTag,
                HardwareAddress, ResourceVersion, session);

            CharacterLoginProfile profile = CreateProfile();
            CharacterPresenceDirectory presence = new();
            connection = new ExistingCharacterGameConnection(authenticatedConnection, profile, presence.Register(CharacterId));

            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            CharacterItemSet itemSet = new(CharacterId, []);
            CharacterSocialRelationSet socialRelationSet = new(CharacterId, []);
            CharacterWeaponSkillSet weaponSkillSet = new(CharacterId, []);
            CharacterMagicSet magicSet = new(CharacterId, []);

            AwaitingSyndicateAttributesConnection awaiting = new(connection, map, itemSet, socialRelationSet, weaponSkillSet, magicSet);

            return new SyndicateFixture(transport, client, presence, profile, map, itemSet, socialRelationSet,
                weaponSkillSet, magicSet, awaiting, transport.SentBytes.Length);
        }
        catch
        {
            client.Dispose();

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
            }

            throw;
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

            string clientPublicKeyHex = Assert.IsType<string>(
                await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));

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

    private static byte[] BuildActionPacket(ushort action, uint entityId, uint parameterPair = 0, uint actionParameter = 0,
        uint timestamp = 0, ushort direction = 0, ushort positionX = 0, ushort positionY = 0, uint data1 = 0,
        uint data2 = 0, byte flag = 0, int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
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
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength)
                .SequenceEqual(GameWireProtocol.ServerSignature));

            packets.Add(plaintext.Slice(offset, header.Length).ToArray());
            offset += wireLength;
        }

        return packets.ToArray();
    }

    private static void AssertSyndicatePacket(byte[] packet)
    {
        Assert.Equal(GameSyndicateAttributeInfoPacket1106.FixedPacketLength, packet.Length);
        Assert.Equal(GameSyndicateAttributeInfoPacket1106.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
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
        Assert.Equal(GameAction10010.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction10010.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction10010.GetSyndicateAttributesAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.All(packet[22..], value => Assert.Equal((byte)0, value));
    }

    private static void AssertFailureBeforeWrite(SyndicateFixture fixture, FakeSyndicateRepository repository)
    {
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingSyndicateAttributes.TakeConnection());
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SyndicateFixture(FakeGameTransportConnection transport, GameClientTestPeer client,
        CharacterPresenceDirectory presence, CharacterLoginProfile profile, GameMapEntryDefinition map,
        CharacterItemSet itemSet, CharacterSocialRelationSet socialRelationSet, CharacterWeaponSkillSet weaponSkillSet,
        CharacterMagicSet magicSet, AwaitingSyndicateAttributesConnection awaitingSyndicateAttributes,
        int responseBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public CharacterPresenceDirectory Presence { get; } = presence;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public CharacterItemSet ItemSet { get; } = itemSet;
        public CharacterSocialRelationSet SocialRelationSet { get; } = socialRelationSet;
        public CharacterWeaponSkillSet WeaponSkillSet { get; } = weaponSkillSet;
        public CharacterMagicSet MagicSet { get; } = magicSet;
        public AwaitingSyndicateAttributesConnection AwaitingSyndicateAttributes { get; } = awaitingSyndicateAttributes;
        public int ResponseBoundary { get; } = responseBoundary;

        public void QueueSyndicateRequest(ushort action = GameAction10010.GetSyndicateAttributesAction, uint? entityId = null,
            uint timestamp = 0)
        {
            QueuePacket(BuildActionPacket(action, entityId ?? CharacterId, timestamp: timestamp));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

        public byte[] DecryptResponse() => Client.DecryptServerBytes(Transport.SentBytes[ResponseBoundary..]);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await AwaitingSyndicateAttributes.DisposeAsync();
            }
        }
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

            if (Failure is not null)
            {
                return ValueTask.FromException<CharacterSyndicateState>(Failure);
            }

            return ValueTask.FromResult(result);
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
