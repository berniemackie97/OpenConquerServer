using System.Buffers.Binary;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Skills;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Skills;

public sealed class ExistingCharacterMagicSetProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint RequestTimestamp = 0x11223344;

    [Fact]
    public void Constructor_NullRepository_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterMagicSetProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesMagicAndExperienceThenAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        CharacterMagicSet magicSet = new(CharacterId,
        [
            CharacterMagic.Create(CharacterId, 2000, ushort.MaxValue, uint.MaxValue),
            CharacterMagic.Create(CharacterId, 1000, 0, 0),
            CharacterMagic.Create(CharacterId, 1500, 4, 123_456),
        ]);
        FakeMagicRepository repository = new(magicSet);

        harness.QueueAction(GameAction.GetMagicSetAction, timestamp: RequestTimestamp);

        await new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Same(magicSet, login.MagicSet);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(6, packets.Length);
        AssertMagicPacket(packets[0], 1000, 0);
        AssertMagicPacket(packets[1], 1500, 4);
        AssertSkillExperiencePacket(packets[2], 123_456, 1500);
        AssertMagicPacket(packets[3], 2000, ushort.MaxValue);
        AssertSkillExperiencePacket(packets[4], uint.MaxValue, 2000);
        AssertAcknowledgement(packets[5], RequestTimestamp);
    }

    [Fact]
    public async Task ProcessAsync_EmptyMagicSet_WritesOnlyAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));

        harness.QueueAction(GameAction.GetMagicSetAction, timestamp: RequestTimestamp);

        await new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        AssertAcknowledgement(acknowledgement, RequestTimestamp);
        Assert.Empty(login.MagicSet!.Magic);
    }

    [Fact]
    public async Task ProcessAsync_ZeroExperience_DoesNotWriteSkillExperience()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, [CharacterMagic.Create(CharacterId, 1000, 1, 0)]));

        harness.QueueAction(GameAction.GetMagicSetAction);

        await new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(2, packets.Length);
        AssertMagicPacket(packets[0], 1000, 1);
        AssertAcknowledgement(packets[1], 0);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutRepositoryLoad()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetMagicSetAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetMagicSetAction, CharacterId, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));

        harness.QueueAction(GameAction.GetMagicSetAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected magic-set request action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));

        harness.QueueAction(GameAction.GetMagicSetAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutRepositoryLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetMagicSetAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

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
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetMagicSetAction, CharacterId, timestamp: RequestTimestamp);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("requires all non-identity, non-timestamp and non-action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryFailure_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException repositoryFailure = new("magic repository failed");
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, [])) { Failure = repositoryFailure };

        harness.QueueAction(GameAction.GetMagicSetAction);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(repositoryFailure, exception);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryReturnsDifferentCharacter_FailsBeforeWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId + 1, []));

        harness.QueueAction(GameAction.GetMagicSetAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("hydrated magic set belongs to character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(CharacterId, repository.LastCharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.MagicSet);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCallCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCallCount, harness.Transport.ReceiveCallCount);
        Assert.Equal(0, repository.LoadCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingRequest_DoesNotLoadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, []));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

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
        BlockingMagicRepository repository = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetMagicSetAction);

        Task processing = new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

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
        CancelingMagicRepository repository = new(new CharacterMagicSet(CharacterId, []), cancellation);

        harness.QueueAction(GameAction.GetMagicSetAction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.MagicSet);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringFirstResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        FakeMagicRepository repository = new(new CharacterMagicSet(CharacterId, [CharacterMagic.Create(CharacterId, 1000, 1, 100)]));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.GetMagicSetAction);
        harness.Transport.BlockSends();

        Task processing = new ExistingCharacterMagicSetProcessor(repository).ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static void AssertMagicPacket(byte[] packet, ushort magicType, ushort level)
    {
        Assert.Equal(GameMagicPacket.FixedPacketLength, packet.Length);
        Assert.Equal(GameMagicPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(magicType, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)));
        Assert.Equal(level, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)));
    }

    private static void AssertSkillExperiencePacket(byte[] packet, uint experience, ushort magicType)
    {
        Assert.Equal(GameSkillExperiencePacket.FixedPacketLength, packet.Length);
        Assert.Equal(GameSkillExperiencePacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(magicType, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12)));
        Assert.Equal(GameSkillExperiencePacket.MagicAction, packet[14]);
        Assert.Equal((byte)0, packet[15]);
    }

    private static void AssertAcknowledgement(byte[] packet, uint timestamp)
    {
        Assert.Equal(GameAction.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(timestamp, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction.GetMagicSetAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(22)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32)));
        Assert.Equal((byte)0, packet[36]);
        Assert.Equal((byte)0, packet[37]);
    }

    private sealed class FakeMagicRepository(CharacterMagicSet result) : ICharacterMagicSetRepository
    {
        private int _loadCount;

        public Exception? Failure { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? LastCharacterId { get; private set; }

        public ValueTask<CharacterMagicSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            LastCharacterId = characterId;

            return Failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<CharacterMagicSet>(Failure);
        }
    }

    private sealed class BlockingMagicRepository : ICharacterMagicSetRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public uint? CharacterId { get; private set; }

        public async ValueTask<CharacterMagicSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            CharacterId = characterId;
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CancelingMagicRepository(CharacterMagicSet result, CancellationTokenSource cancellation) : ICharacterMagicSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? CharacterId { get; private set; }

        public ValueTask<CharacterMagicSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            CharacterId = characterId;
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
