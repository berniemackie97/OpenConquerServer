using System.Buffers.Binary;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Skills;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Skills;

public sealed class ExistingCharacterWeaponSkillSetProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint RequestTimestamp = 0x11223344;

    [Fact]
    public void Constructor_NullRepository_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterWeaponSkillSetProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesSkillsThenAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterWeaponSkillSet skillSet = new(CharacterId,
        [
            WeaponSkill.Create(CharacterId, 1050, 20, uint.MaxValue),
            WeaponSkill.Create(CharacterId, 500, 3, 250_000),
            WeaponSkill.Create(CharacterId, 410, 1, 1_200),
        ]);
        FakeWeaponSkillRepository repository = new(skillSet);

        harness.QueueAction(GameAction.GetWeaponSkillSetAction, timestamp: RequestTimestamp);

        await new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Same(skillSet, login.WeaponSkillSet);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(4, packets.Length);
        AssertWeaponSkillPacket(packets[0], 410, 1, 1_200, 1_200);
        AssertWeaponSkillPacket(packets[1], 500, 3, 250_000, 250_000);
        AssertWeaponSkillPacket(packets[2], 1050, 20, uint.MaxValue, 0);
        AssertAcknowledgement(packets[3], RequestTimestamp);
    }

    [Fact]
    public async Task ProcessAsync_EmptySkillSet_WritesOnlyAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));

        harness.QueueAction(GameAction.GetWeaponSkillSetAction, timestamp: RequestTimestamp);

        await new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        AssertAcknowledgement(acknowledgement, RequestTimestamp);
        Assert.Empty(login.WeaponSkillSet!.Skills);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutRepositoryLoad()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetWeaponSkillSetAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetWeaponSkillSetAction, CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));

        harness.QueueAction(GameAction.GetWeaponSkillSetAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected weapon-skill-set request action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));

        harness.QueueAction(GameAction.GetWeaponSkillSetAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetWeaponSkillSetAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

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
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetWeaponSkillSetAction, CharacterId, timestamp: RequestTimestamp);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("requires all non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException repositoryFailure = new("weapon-skill repository failed");
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, [])) { Failure = repositoryFailure };

        harness.QueueAction(GameAction.GetWeaponSkillSetAction);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(repositoryFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId + 1, []));

        harness.QueueAction(GameAction.GetWeaponSkillSetAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated weapon-skill set belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.WeaponSkillSet);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCount, harness.Transport.ReceiveCallCount);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

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
        BlockingWeaponSkillRepository repository = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetWeaponSkillSetAction);

        Task processing = new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

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
        CancelingWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, []), cancellation);

        harness.QueueAction(GameAction.GetWeaponSkillSetAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.WeaponSkillSet);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeWeaponSkillRepository repository = new(new CharacterWeaponSkillSet(CharacterId, [WeaponSkill.Create(CharacterId, 410, 1, 1_200)]));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetWeaponSkillSetAction);
        harness.Transport.BlockSends();

        Task processing = new ExistingCharacterWeaponSkillSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static void AssertWeaponSkillPacket(byte[] packet, uint type, ushort level, uint experience, uint nextLevelExperienceRequirement)
    {
        Assert.Equal(GameWeaponSkillPacket.FixedPacketLength, packet.Length);
        Assert.Equal(GameWeaponSkillPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(type, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(level, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(nextLevelExperienceRequirement, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
    }

    private static void AssertAcknowledgement(byte[] packet, uint timestamp)
    {
        Assert.Equal(GameAction.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction.GetWeaponSkillSetAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(22)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32)));
        Assert.Equal((byte)0, packet[36]);
        Assert.Equal((byte)0, packet[37]);
    }

    private sealed class FakeWeaponSkillRepository(CharacterWeaponSkillSet result) : ICharacterWeaponSkillSetRepository
    {
        private int _loadCount;

        public Exception? Failure { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? LastCharacterId { get; private set; }

        public ValueTask<CharacterWeaponSkillSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            LastCharacterId = characterId;

            return Failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<CharacterWeaponSkillSet>(Failure);
        }
    }

    private sealed class BlockingWeaponSkillRepository : ICharacterWeaponSkillSetRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }

        public async ValueTask<CharacterWeaponSkillSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingWeaponSkillRepository(CharacterWeaponSkillSet result, CancellationTokenSource cancellation) : ICharacterWeaponSkillSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? CharacterId { get; private set; }

        public ValueTask<CharacterWeaponSkillSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            CharacterId = characterId;
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
