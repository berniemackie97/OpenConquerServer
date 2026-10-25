using System.Buffers.Binary;
using System.Net;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
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
        const uint authenticationKey = 0x12345678;
        connection.ArmRepeatedLoginProof(new GameLoginProof(connection.SessionUid, authenticationKey,
            GameLoginProof.ExpectedMode, connection.LocaleTag, connection.HardwareAddress, connection.ResourceVersion));
        byte[] create = new byte[GameCharacterCreationRequest.PacketLength];
        WireFrameHeader.Write(create, GameCharacterCreationRequest.PacketLength, GameCharacterCreationRequest.PacketId);
        "NewHero"u8.CopyTo(create.AsSpan(24));
        BinaryPrimitives.WriteUInt16LittleEndian(create.AsSpan(72), 1003);
        BinaryPrimitives.WriteUInt16LittleEndian(create.AsSpan(74), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(create.AsSpan(76), connection.SessionUid);
        fixture.Harness.QueuePacket(create);
        byte[] proof = new byte[GameLoginProof.PacketLength];
        WireFrameHeader.Write(proof, GameLoginProof.PacketLength, GameLoginProof.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(proof.AsSpan(4), connection.SessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(proof.AsSpan(8), authenticationKey);
        BinaryPrimitives.WriteUInt16LittleEndian(proof.AsSpan(12), GameLoginProof.ExpectedMode);
        fixture.Harness.QueuePacket(proof);
        if (!blockResolution)
        {
            for (int step = 0; step <= ItemsStep; step++)
            {
                fixture.QueueRequest(step);
            }
        }

        CharacterCreationStartingState state = new(1002, 0, 0,
        [
            new(1, 5, 2, 3, 0), new(2, 5, 2, 3, 0), new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0), new(6, 3, 1, 4, 2), new(10, 0, 2, 3, 5),
        ]);
        CreationStore store = new();
        CreationResolver resolver = new(fixture.Profile, blockResolution);
        GameNetworkOptions options = new(new IPEndPoint(IPAddress.Loopback, 5816), 8, 8, 2, 1, TimeSpan.FromSeconds(1));
        using GameCharacterCreationLoginProcessor processor = new(new CharacterCreationService(store, state), resolver,
            fixture.Processor, options);
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

    private sealed class CreationStore : ICharacterCreationStore
    {
        public int CallCount { get; private set; }
        public ValueTask<CharacterCreationStoreResult> CreateAsync(CharacterCreationCandidate character, CancellationToken cancellationToken = default)
        {
            Assert.Equal(42u, character.AccountId);
            CallCount++;
            return ValueTask.FromResult(CharacterCreationStoreResult.Created(CharacterId));
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
