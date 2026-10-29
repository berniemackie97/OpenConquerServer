using System.Buffers.Binary;
using System.Net;
using System.Text;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Hosting;

public sealed partial class GameExistingCharacterLoginProcessorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Creation_AcceptedProofStartsConfiguredTimeoutForResolutionAndBootstrap(bool blockResolution)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync(blockedLoad: "items");
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        fixture.Harness.QueuePacket(CreateRoleRequest(connection.SessionUid));
        fixture.Harness.QueuePacket(CreateRepeatedProof(connection.SessionUid));
        if (!blockResolution)
        {
            for (int step = 0; step <= ItemsStep; step++)
            {
                fixture.QueueRequest(step);
            }
        }

        CreationStore store = new();
        CreationResolver resolver = new(fixture.Profile, blockResolution);
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, resolver, TimeSpan.FromSeconds(1));
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task processing = processor.ProcessAsync(connection, stopping.Token);
        try
        {
            await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            if (!blockResolution)
            {
                await fixture.Store.LoadBlocked.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            }
            // The old proof deadline lasts 30 seconds; the configured continuation deadline must win.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
            Assert.Equal(1, store.CallCount);
            Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
            Assert.False(fixture.Presence.IsOnline(CharacterId));
        }
        finally
        {
            stopping.Cancel();
            try
            {
                await processing;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_AcceptedOrRetriedRequest_ContinuesEncryptedConnectionThroughWorldAdmission(bool retryName)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        CreationStore store = new(retryName ? CharacterCreationStoreResult.NameAlreadyInUse() : null);
        CreationResolver resolver = new(fixture.Profile, false);
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, resolver);
        fixture.Harness.QueuePacket(CreateRoleRequest(connection.SessionUid));
        if (retryName)
        {
            fixture.Harness.QueuePacket(CreateRoleRequest(connection.SessionUid));
        }
        fixture.Harness.QueuePacket(CreateRepeatedProof(connection.SessionUid));
        for (int step = 0; step < s_loadsByStep.Length; step++)
        {
            fixture.QueueRequest(step);
        }

        await using WorldSession session = Assert.IsType<WorldSession>(await processor.ProcessAsync(connection, TestContext.Current.CancellationToken));
        byte[][] responses = fixture.Harness.ReadServerPackets();
        AssertCreationMessage(responses[0], 0x835, "NEW_ROLE");
        int acceptedIndex = retryName ? 2 : 1;
        if (retryName)
        {
            AssertCreationMessage(responses[1], 0x834, "Character name is already in use.");
        }
        AssertCreationMessage(responses[acceptedIndex], 0x834, "ANSWER_OK");
        Assert.Equal(s_expectedResponses.SelectMany(static step => step), responses.Skip(acceptedIndex + 1).Select(Convert.ToHexString));
        Assert.Equal(retryName ? 2 : 1, store.CallCount);
        Assert.Single(fixture.AccountSessions.Entered);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.NotNull(await fixture.Runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Harness.Transport.DisposeCount);
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreationProof(connection)));

        await session.DisposeAsync();
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.Single(fixture.AccountSessions.Left);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Null(await fixture.Runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("session")]
    [InlineData("malformed")]
    [InlineData("disconnect")]
    public async Task Creation_InvalidRequest_ClosesWithoutPersistenceOrAdmission(string reason)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        CreationStore store = new();
        CreationResolver resolver = new(fixture.Profile, false);
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, resolver);
        byte[] request = CreateRoleRequest(reason == "session" ? connection.SessionUid + 1 : connection.SessionUid);
        if (reason == "cancel")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 1);
        }
        if (reason == "malformed")
        {
            request[8] = 1;
        }
        if (reason == "disconnect")
        {
            fixture.Harness.Transport.QueueEndOfStream();
        }
        else
        {
            fixture.Harness.QueuePacket(request);
        }

        Assert.Null(await processor.ProcessAsync(connection, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.CallCount);
        Assert.False(resolver.Entered.Task.IsCompleted);
        Assert.Empty(fixture.AccountSessions.Entered);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        AssertCreationMessage(Assert.Single(fixture.Harness.ReadServerPackets()), 0x835, "NEW_ROLE");
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreationProof(connection)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_InvalidOrMissingRepeatProof_DoesNotResolveOrAdmitCommittedCharacter(bool missing)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        CreationStore store = new();
        CreationResolver resolver = new(fixture.Profile, false);
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, resolver);
        fixture.Harness.QueuePacket(CreateRoleRequest(connection.SessionUid));
        if (missing)
        {
            fixture.Harness.Transport.QueueEndOfStream();
        }
        else
        {
            fixture.Harness.QueuePacket(CreateRepeatedProof(connection.SessionUid, authenticationKey: 1));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(connection, TestContext.Current.CancellationToken));
        Assert.Equal(1, store.CallCount);
        Assert.False(resolver.Entered.Task.IsCompleted);
        Assert.Empty(fixture.AccountSessions.Entered);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        byte[][] responses = fixture.Harness.ReadServerPackets();
        Assert.Equal(2, responses.Length);
        AssertCreationMessage(responses[1], 0x834, "ANSWER_OK");
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreationProof(connection)));
    }

    [Fact]
    public async Task Creation_RepeatedInvalidSelections_StopAfterEightAttemptsWithoutPersistence()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        CreationStore store = new();
        CreationResolver resolver = new(fixture.Profile, false);
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, resolver);
        for (int attempt = 0; attempt < 9; attempt++)
        {
            byte[] request = CreateRoleRequest(connection.SessionUid);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(72), 999);
            fixture.Harness.QueuePacket(request);
        }

        Assert.Null(await processor.ProcessAsync(connection, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.CallCount);
        Assert.False(resolver.Entered.Task.IsCompleted);
        byte[][] responses = fixture.Harness.ReadServerPackets();
        Assert.Equal(9, responses.Length);
        Assert.All(responses.Skip(1), packet => AssertCreationMessage(packet, 0x834, "Invalid character selection."));
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
    }

    [Fact]
    public async Task Creation_FailedBootstrapCleanup_RemainsFatalAfterConnectionHandoff()
    {
        IOException cleanupFailure = new("transport disposal failed after creation");
        await using LoginFixture fixture = await LoginFixture.CreateAsync(disposeFailure: cleanupFailure);
        AuthenticatedGameConnection connection = fixture.Handoff.TakeConnection();
        ArmCreationProof(connection);
        CreationStore store = new();
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(fixture, store, new CreationResolver(fixture.Profile, false));
        fixture.Harness.QueuePacket(CreateRoleRequest(connection.SessionUid));
        fixture.Harness.QueuePacket(CreateRepeatedProof(connection.SessionUid));
        fixture.Harness.QueueAction(UnexpectedAction);

        GameLoginCleanupException failure = await Assert.ThrowsAsync<GameLoginCleanupException>(() => processor.ProcessAsync(connection, TestContext.Current.CancellationToken));
        Assert.Contains(cleanupFailure, failure.Flatten().InnerExceptions);
        Assert.Contains(failure.Flatten().InnerExceptions, exception => exception is InvalidDataException);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.Single(fixture.AccountSessions.Left);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        await AssertNotInWorldAsync(fixture);
    }

    [Fact]
    public async Task Creation_CapacityRefusalAndCancellation_ReleaseConnectionsAndRestoreCapacity()
    {
        await using LoginFixture first = await LoginFixture.CreateAsync();
        await using LoginFixture rejected = await LoginFixture.CreateAsync();
        await using LoginFixture next = await LoginFixture.CreateAsync();
        CreationStore store = new();
        using GameCharacterCreationLoginProcessor processor = CreateCreationProcessor(first, store, new CreationResolver(first.Profile, false), capacity: 1);
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<WorldSession?> pending = processor.ProcessAsync(first.Handoff.TakeConnection(), stopping.Token);
        await first.Harness.Transport.ReceiveStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        try
        {
            Assert.Null(await processor.ProcessAsync(rejected.Handoff.TakeConnection(), TestContext.Current.CancellationToken));
            Assert.Equal(1, rejected.Harness.Transport.DisposeCount);
            Assert.Empty(rejected.Harness.ReadServerPackets());
        }
        finally
        {
            stopping.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.Equal(1, first.Harness.Transport.DisposeCount);
        next.Harness.Transport.QueueEndOfStream();
        Assert.Null(await processor.ProcessAsync(next.Handoff.TakeConnection(), TestContext.Current.CancellationToken));
        AssertCreationMessage(Assert.Single(next.Harness.ReadServerPackets()), 0x835, "NEW_ROLE");
        Assert.Equal(1, next.Harness.Transport.DisposeCount);
        Assert.Equal(0, store.CallCount);
    }

    private static GameCharacterCreationLoginProcessor CreateCreationProcessor(LoginFixture fixture, ICharacterCreationStore store,
        ICharacterLoginResolver resolver, TimeSpan? timeout = null, int capacity = 8)
    {
        CharacterCreationStartingState state = new(1002, 0, 0,
        [
            new(1, 5, 2, 3, 0), new(2, 5, 2, 3, 0), new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0), new(6, 3, 1, 4, 2), new(10, 0, 2, 3, 5),
        ]);
        GameNetworkOptions options = new(new IPEndPoint(IPAddress.Loopback, 5816), 8, capacity, 2, 1, timeout ?? TimeSpan.FromSeconds(30));
        return new GameCharacterCreationLoginProcessor(new CharacterCreationService(store, state), resolver, fixture.Processor, options);
    }

    private static GameLoginProof CreationProof(AuthenticatedGameConnection connection) => new(connection.SessionUid,
        0x12345678, GameLoginProof.ExpectedMode, connection.LocaleTag, connection.HardwareAddress, connection.ResourceVersion);

    private static void ArmCreationProof(AuthenticatedGameConnection connection) => connection.ArmRepeatedLoginProof(CreationProof(connection));

    private static byte[] CreateRoleRequest(uint sessionUid)
    {
        byte[] request = new byte[GameCharacterCreationRequest.PacketLength];
        WireFrameHeader.Write(request, GameCharacterCreationRequest.PacketLength, GameCharacterCreationRequest.PacketId);
        "NewHero"u8.CopyTo(request.AsSpan(24));
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(72), 1003);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(74), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(76), sessionUid);
        return request;
    }

    private static byte[] CreateRepeatedProof(uint sessionUid, uint authenticationKey = 0x12345678)
    {
        byte[] proof = new byte[GameLoginProof.PacketLength];
        WireFrameHeader.Write(proof, GameLoginProof.PacketLength, GameLoginProof.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(proof.AsSpan(4), sessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(proof.AsSpan(8), authenticationKey);
        BinaryPrimitives.WriteUInt16LittleEndian(proof.AsSpan(12), GameLoginProof.ExpectedMode);
        return proof;
    }

    private static void AssertCreationMessage(byte[] packet, ushort channel, string message)
    {
        Assert.Equal(GameChatPacket.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
        Assert.Equal(channel, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)));
        Assert.Equal(message, Encoding.ASCII.GetString(packet.AsSpan(29)));
    }

    private sealed class CreationStore(CharacterCreationStoreResult? firstResult = null) : ICharacterCreationStore
    {
        public int CallCount { get; private set; }
        public ValueTask<CharacterCreationStoreResult> CreateAsync(CharacterCreationCandidate character, CancellationToken cancellationToken = default)
        {
            Assert.Equal(42u, character.AccountId);
            CallCount++;
            return ValueTask.FromResult(CallCount == 1 && firstResult.HasValue ? firstResult.Value : CharacterCreationStoreResult.Created(CharacterId));
        }
    }

    private sealed class CreationResolver(CharacterLoginProfile profile, bool block) : ICharacterLoginResolver
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(profile.Identity.AccountId, accountId);
            Entered.SetResult();
            if (block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return CharacterLoginResolution.ExistingCharacter(profile);
        }
    }
}
