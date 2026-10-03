using System.Buffers.Binary;
using System.Net;
using System.Text;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;
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

public sealed class ExistingCharacterFriendListProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint FriendId = CharacterId + 1;
    private const uint EnemyId = CharacterId + 2;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;
    private const uint RequestTimestamp = 0x11223344;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        CharacterPresenceDirectory presence = new();

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterFriendListProcessor(null!, presence));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterFriendListProcessor(repository, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesRelationsThenAcknowledgementAndTransfersConnectionExactlyOnce()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        CharacterSocialRelationSet relationSet = new(CharacterId,
        [
            CreateRelation(EnemyId, SocialRelationKind.Enemy, "Rival"),
            CreateRelation(FriendId, SocialRelationKind.Friend, "Ally"),
        ]);
        FakeSocialRepository repository = new(relationSet);
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        using ICharacterPresenceLease friendPresence = fixture.Presence.Register(FriendId);

        fixture.QueueFriendListRequest(timestamp: RequestTimestamp);

        AwaitingWeaponSkillSetConnection result = await processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken);
        ExistingCharacterGameConnection transferredConnection = result.TakeConnection();

        Assert.Same(fixture.Profile, transferredConnection.Profile);
        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Same(fixture.ItemSet, result.ItemSet);
        Assert.Same(relationSet, result.SocialRelationSet);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingFriendList.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());

        byte[][] packets = ReadPackets(fixture.DecryptFriendListResponse());

        Assert.Equal(3, packets.Length);
        AssertRelationPacket(packets[0], FriendId, GameSocialRelationAction.AddFriendSilently, stateFlag: 1, "Ally");
        AssertRelationPacket(packets[1], EnemyId, GameSocialRelationAction.AddEnemy, stateFlag: 0, "Rival");
        AssertAcknowledgement(packets[2], CharacterId, RequestTimestamp);

        await result.DisposeAsync();

        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await transferredConnection.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_EmptyRelationSet_WritesOnlyAcknowledgement()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest(timestamp: RequestTimestamp);

        AwaitingWeaponSkillSetConnection result = await processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(ReadPackets(fixture.DecryptFriendListResponse()));
        AssertAcknowledgement(acknowledgement, CharacterId, RequestTimestamp);
        Assert.Empty(result.SocialRelationSet.Relations);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(0, fixture.Transport.DisposeCount);

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_MoreThanFiftyRelations_WritesEveryRelationAndAcknowledgement()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        CharacterSocialRelation[] relations = Enumerable.Range(1, 51)
            .Select(index => CreateRelation(CharacterId + checked((uint)index), SocialRelationKind.Friend, $"Friend{index}"))
            .ToArray();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, relations));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest(timestamp: RequestTimestamp);

        AwaitingWeaponSkillSetConnection result = await processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken);
        byte[][] packets = ReadPackets(fixture.DecryptFriendListResponse());

        Assert.Equal(52, packets.Length);
        Assert.All(packets.Take(51), packet => Assert.Equal(GameSocialRelation1019.PacketIdentifier, ReadPacketId(packet)));
        AssertAcknowledgement(packets[51], CharacterId, RequestTimestamp);
        Assert.Equal(51, result.SocialRelationSet.Count);

        await result.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeFriendListRequest_DisposesConnectionWithoutRepositoryLoad()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingFriendList.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        byte[] packet = BuildActionPacket(GameAction10010.GetGoodFriendAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueuePacket(BuildActionPacket(GameAction10010.GetGoodFriendAction, CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest(action: GameAction10010.GetGoodFriendAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected friend-list action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest(entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_FriendListRequestContainsTrailingStrings_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        byte[] packet = BuildActionPacket(GameAction10010.GetGoodFriendAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
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
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        byte[] packet = BuildActionPacket(GameAction10010.GetGoodFriendAction, CharacterId, timestamp: RequestTimestamp);

        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("requires all non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothingAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        IOException repositoryFailure = new("social repository failed");
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, [])) { Failure = repositoryFailure };
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();

        IOException exception = await Assert.ThrowsAsync<IOException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(repositoryFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        uint otherCharacterId = CharacterId + 1;
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(otherCharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated social-relation set belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ProjectionFailure_WritesNothingAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        InvalidOperationException projectionFailure = new("presence lookup failed");
        CharacterSocialRelationSet relationSet = new(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]);
        FakeSocialRepository repository = new(relationSet);
        ExistingCharacterFriendListProcessor processor = new(repository, new ThrowingPresenceReader(projectionFailure));

        fixture.QueueFriendListRequest();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(projectionFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWriteAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCountBeforeProcessing = fixture.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCountBeforeProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotLoadOrWriteAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingWeaponSkillSetConnection> processing = processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRepositoryLoad_WritesNothingAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        BlockingSocialRepository repository = new();
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueFriendListRequest();

        Task<AwaitingWeaponSkillSetConnection> processing = processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask();

        await repository.Started.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationImmediatelyAfterRepositoryReturn_WritesNothingAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []), cancellation);
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringProjection_StopsBeforeFirstWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CharacterSocialRelationSet relationSet = new(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]);
        FakeSocialRepository repository = new(relationSet);
        ExistingCharacterFriendListProcessor processor = new(repository, new CancelingPresenceReader(cancellation));

        fixture.QueueFriendListRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationDuringRepositoryLoad_CancelsOldSessionWithoutRemovingReplacement()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        BlockingSocialRepository repository = new();
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();

        Task<AwaitingWeaponSkillSetConnection> processing = processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask();

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
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_WritesNoPartialPacketAndDisposesConnection()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        CharacterSocialRelationSet relationSet = new(CharacterId, [CreateRelation(FriendId, SocialRelationKind.Friend, "Ally")]);
        FakeSocialRepository repository = new(relationSet);
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueFriendListRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingWeaponSkillSetConnection> processing = processor.ProcessAsync(fixture.AwaitingFriendList, cancellation.Token).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        fixture.Transport.ReleaseSends();

        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingFriendList.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfAwaitingStateIsRejectedWithoutSecondReadLoadOrWrite()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();

        AwaitingWeaponSkillSetConnection result = await processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken);
        int sentLengthAfterFirstProcessing = fixture.Transport.SentBytes.Length;
        int receiveCallCountAfterFirstProcessing = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLengthAfterFirstProcessing, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCallCountAfterFirstProcessing, fixture.Transport.ReceiveCallCount);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await result.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfAwaitingStateAllowsExactlyOneOwner()
    {
        await using FriendListFixture fixture = await CreateFixtureAsync();
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest();
        fixture.Transport.BlockSends();

        Task<AwaitingWeaponSkillSetConnection> first = processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(1, repository.LoadCount);
            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        AwaitingWeaponSkillSetConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Same(fixture.ItemSet, result.ItemSet);
        Assert.Empty(result.SocialRelationSet.Relations);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Single(ReadPackets(fixture.DecryptFriendListResponse()));

        await result.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using FriendListFixture fixture = await CreateFixtureAsync(cleanupFailure);
        FakeSocialRepository repository = new(new CharacterSocialRelationSet(CharacterId, []));
        ExistingCharacterFriendListProcessor processor = new(repository, fixture.Presence);

        fixture.QueueFriendListRequest(action: GameAction10010.GetGoodFriendAction + 1);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(fixture.AwaitingFriendList, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    private static async Task<FriendListFixture> CreateFixtureAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);
        ExistingCharacterGameConnection? connection = null;

        try
        {
            AuthenticatedGameConnection authenticatedConnection = new(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
            CharacterLoginProfile profile = CreateProfile();
            CharacterPresenceDirectory presence = new();
            connection = new ExistingCharacterGameConnection(authenticatedConnection, profile, presence.Register(CharacterId));
            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            CharacterItemSet itemSet = new(CharacterId, []);
            AwaitingFriendListConnection awaitingFriendList = new(connection, map, itemSet);

            return new FriendListFixture(transport, client, presence, profile, map, itemSet, awaitingFriendList, transport.SentBytes.Length);
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

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount: 2, preRebirthLevel: 130);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(MapId, x: 430, y: 378);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25, titleId: 321, enlightenmentPoints: 1234, location);
    }

    private static CharacterSocialRelation CreateRelation(uint counterpartCharacterId, SocialRelationKind kind, string name)
    {
        return new CharacterSocialRelation(SocialRelation.Create(CharacterId, counterpartCharacterId, kind), name);
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

    private static void AssertRelationPacket(byte[] packet, uint entityId, GameSocialRelationAction action, byte stateFlag, string name)
    {
        Assert.Equal(GameSocialRelation1019.FixedPacketLength, packet.Length);
        Assert.Equal(GameSocialRelation1019.PacketIdentifier, ReadPacketId(packet));
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

    private static void AssertAcknowledgement(byte[] packet, uint characterId, uint timestamp)
    {
        Assert.Equal(GameAction10010.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction10010.PacketIdentifier, ReadPacketId(packet));
        Assert.Equal(characterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction10010.GetGoodFriendAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(22)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32)));
        Assert.Equal((byte)0, packet[36]);
        Assert.Equal((byte)0, packet[37]);
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

    private sealed class FriendListFixture(FakeGameTransportConnection transport, GameClientTestPeer client, CharacterPresenceDirectory presence, CharacterLoginProfile profile, GameMapEntryDefinition map, CharacterItemSet itemSet, AwaitingFriendListConnection awaitingFriendList, int responseBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public CharacterPresenceDirectory Presence { get; } = presence;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public CharacterItemSet ItemSet { get; } = itemSet;
        public AwaitingFriendListConnection AwaitingFriendList { get; } = awaitingFriendList;
        public int ResponseBoundary { get; } = responseBoundary;

        public void QueueFriendListRequest(ushort action = GameAction10010.GetGoodFriendAction, uint? entityId = null, uint timestamp = 0)
        {
            QueuePacket(BuildActionPacket(action, entityId ?? CharacterId, timestamp: timestamp));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

        public byte[] DecryptFriendListResponse() => Client.DecryptServerBytes(Transport.SentBytes[ResponseBoundary..]);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await AwaitingFriendList.DisposeAsync();
            }
        }
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

            if (Failure is not null)
            {
                return ValueTask.FromException<CharacterSocialRelationSet>(Failure);
            }

            return ValueTask.FromResult(result);
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
