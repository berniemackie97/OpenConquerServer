using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.World.Chat;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.Tests.World.Chat;

/// <summary>
/// Exercises world-chat fanout using admitted sessions and the same negotiated,
/// encrypted game transport used by the production session writer. No test-only
/// publish hooks or alternative delivery paths are involved.
/// </summary>
public sealed class WorldChatDeliveryTests
{
    private const uint FirstMap = 1002;
    private const uint SecondMap = 1003;
    private const uint FirstCharacter = CharacterIdentityPolicy.FirstPlayerEntityId;

    [Fact]
    public async Task LocalSpeech_ReachesOnlyPlayersOnTheVisibleMap()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bob = await world.AddAsync(1, "Bobby", FirstMap);
        ClientSession carol = await world.AddAsync(2, "Carol", SecondMap);
        ClientSession dave = await world.AddAsync(3, "Dave", FirstMap, 40, 40);
        GameInboundChat request = new(GameInboundChat.LocalChannel, "ALL", "local message");
        GameChatPacket expected = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "ALL", request);

        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);

        await alice.ExpectPacketAsync(expected);
        await bob.ExpectPacketAsync(expected);
        Assert.DoesNotContain(carol.AvailableFrames(), frame => FrameMatches(frame, expected));
        Assert.DoesNotContain(dave.AvailableFrames(), frame => FrameMatches(frame, expected));
    }

    [Fact]
    public async Task NativeTwoTileMovement_RelaysUnmodifiedPacketToVisibleObserver()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 1, 1);
        await bobby.WaitForFrameCountAsync(1);

        GameWalkPacket movement = new(0xA1B2_C306, alice.Session.Ownership.CharacterId,
            GameWalkPacket.TwentyFourDirectionSelector, 100, 0xDEAD_BEEFu);
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(movement)];
        GameWireFrameEncoder.WriteFrame(movement, expected);

        await alice.Session.ProcessWalkAsync(movement, TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> observed = await bobby.WaitForFrameCountAsync(2);
        Assert.Contains(observed, frame => frame.AsSpan().SequenceEqual(expected));
        IReadOnlyList<byte[]> echoed = await alice.WaitForFrameCountAsync(2);
        Assert.Contains(echoed, frame => frame.AsSpan().SequenceEqual(expected));
    }

    [Fact]
    public async Task LocalSpeech_ReachesNewObserverAfterNativeTwoTileMovement()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 20, 0);
        Assert.Empty(alice.AvailableFrames());
        Assert.Empty(bobby.AvailableFrames());

        GameWalkPacket movement = new(6, alice.Session.Ownership.CharacterId,
            GameWalkPacket.TwentyFourDirectionSelector, 100, 0);
        await alice.Session.ProcessWalkAsync(movement, TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> observed = await bobby.WaitForFrameCountAsync(1);
        Assert.Equal((ushort)10014, PacketId(observed[0]));

        GameInboundChat chat = new(GameInboundChat.LocalChannel, "ALL", "now nearby");
        GameChatPacket expected = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId,
            "Alice", "ALL", chat);
        await alice.Session.ProcessChatAsync(chat, TestContext.Current.CancellationToken);
        await bobby.ExpectPacketAsync(expected);
    }

    [Fact]
    public async Task NativeJump_AcknowledgesAndRelaysOnlyAfterAuthoritativeLanding()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 16, 0);
        ClientSession carol = await world.AddAsync(2, "Carol", SecondMap);
        GameAction action = new(alice.Session.Ownership.CharacterId, 16, 0x1234, 100,
            GameJumpAction.JumpAction, 6, 0, 0, 0, 0, 0, 0);
        GameJumpAction jump = new(action);
        GameActionPacket echo = jump.CreateAcknowledgement();
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(echo)];
        GameWireFrameEncoder.WriteFrame(echo, expected);

        await alice.Session.ProcessJumpAsync(jump, TestContext.Current.CancellationToken);
        await alice.ExpectFrameAsync(expected);
        await bobby.ExpectFrameAsync(expected);
        Assert.DoesNotContain(carol.AvailableFrames(), frame => frame.AsSpan().SequenceEqual(expected));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MovementCrossingVisibilityBoundary_RelaysFinalMotionToDepartingObserver(bool jump)
    {
        await using ChatWorld world = new();
        // The two players are initially visible at exactly the native radius.
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 18, 1);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 0, 1);
        await bobby.WaitForFrameCountAsync(1);

        byte[] expected;
        if (jump)
        {
            // Native absolute target: (34, 1), 16 tiles from Alice, now 34
            // tiles from Bobby. A retained-only fanout silently loses this.
            GameAction action = new(alice.Session.Ownership.CharacterId, (1u << 16) | 34u,
                0, 100, GameJumpAction.JumpAction, 6, 0, 0, 0, 0, 0, 0);
            GameJumpAction intent = new(action);
            GameActionPacket acknowledgement = intent.CreateAcknowledgement();
            expected = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
            GameWireFrameEncoder.WriteFrame(acknowledgement, expected);
            await alice.Session.ProcessJumpAsync(intent, TestContext.Current.CancellationToken);
        }
        else
        {
            // A one-tile eastward step crosses 18 -> 19 tiles.
            GameWalkPacket intent = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);
            expected = new byte[GameWireFrameEncoder.GetFrameLength(intent)];
            GameWireFrameEncoder.WriteFrame(intent, expected);
            await alice.Session.ProcessWalkAsync(intent, TestContext.Current.CancellationToken);
        }

        await bobby.ExpectFrameAsync(expected);
        Assert.Single(bobby.AvailableFrames(), frame => frame.AsSpan().SequenceEqual(expected));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedSenderSocket_DoesNotDelayAuthoritativeMovementOrPeerDelivery(bool jump)
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 12, 0);

        // Finish the initial bidirectional spawn before blocking the mover's
        // outgoing transport. A live socket must never hold the map scheduler.
        await alice.WaitForFrameCountAsync(1);
        await bobby.WaitForFrameCountAsync(1);
        alice.Transport.BlockSends();

        byte[] expected;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        if (jump)
        {
            GameJumpAction intent = new(new GameAction(alice.Session.Ownership.CharacterId,
                6, 0, 100, GameJumpAction.JumpAction, 6, 0, 0, 0, 0, 0, 0));
            GameActionPacket acknowledgement = intent.CreateAcknowledgement();
            expected = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
            GameWireFrameEncoder.WriteFrame(acknowledgement, expected);
            await alice.Session.ProcessJumpAsync(intent, timeout.Token);
        }
        else
        {
            GameWalkPacket intent = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);
            expected = new byte[GameWireFrameEncoder.GetFrameLength(intent)];
            GameWireFrameEncoder.WriteFrame(intent, expected);
            await alice.Session.ProcessWalkAsync(intent, timeout.Token);
        }

        // The peer sees the committed movement although the mover's self-echo
        // is stalled on the network. No second reader or synthetic delivery hook.
        await bobby.ExpectFrameAsync(expected);
        Assert.False(bobby.Session.IsStopping);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedPredictedMovement_ImmediatelySendsNative108AuthoritativePosition(bool jump)
    {
        await using ChatWorld world = new();
        // A blocked west step at x=0, or an in-range jump off the map
        // from x=63, exercises the authoritative partition rejection path.
        ushort startX = jump ? (ushort)63 : (ushort)0;
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, startX, 0);
        if (jump)
        {
            GameJumpAction intent = new(new GameAction(alice.Session.Ownership.CharacterId,
                64, 0, 100, GameJumpAction.JumpAction, 6, 0, 0, 0, 0, 0, 0));
            await alice.Session.ProcessJumpAsync(intent, TestContext.Current.CancellationToken);
        }
        else
        {
            GameWalkPacket intent = new(2, alice.Session.Ownership.CharacterId, 0, 100, 0);
            await alice.Session.ProcessWalkAsync(intent, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<byte[]> received = await alice.WaitForFrameCountAsync(1);
        byte[] correction = Assert.Single(received);
        Assert.Equal((ushort)10010, PacketId(correction));
        Assert.Equal((ushort)108, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(20)));
        Assert.Equal(startX, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(24)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(26)));
        Assert.Equal(alice.Session.Ownership.CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(correction.AsSpan(4)));
    }

    [Fact]
    public async Task RejectedMovementReplay_CorrectsToLastCommittedTile()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 10, 10);
        GameWalkPacket first = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);
        GameWalkPacket second = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);

        await alice.Session.ProcessWalkAsync(first, TestContext.Current.CancellationToken);
        await alice.Session.ProcessWalkAsync(second, TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> received = await alice.WaitForFrameCountAsync(2);
        Assert.Equal(2, received.Count);
        Assert.Equal(GameWalkPacket.PacketIdentifier, PacketId(received[0]));
        Assert.Equal((ushort)10010, PacketId(received[1]));
        Assert.Equal((ushort)108, BinaryPrimitives.ReadUInt16LittleEndian(received[1].AsSpan(20)));
        Assert.Equal((ushort)11, BinaryPrimitives.ReadUInt16LittleEndian(received[1].AsSpan(24)));
        Assert.Equal((ushort)10, BinaryPrimitives.ReadUInt16LittleEndian(received[1].AsSpan(26)));
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)7)]
    public async Task MountedChargeOrUnknownVariant_CorrectsRatherThanEchoes(byte flag)
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 10, 10);
        GameJumpAction intent = new(new GameAction(alice.Session.Ownership.CharacterId,
            (10u << 16) | 14u, 0, 100, GameJumpAction.JumpAction, 6, 0, 0, 0, 0,
            flag, 0));
        await alice.Session.ProcessJumpAsync(intent, TestContext.Current.CancellationToken);

        byte[] correction = Assert.Single(await alice.WaitForFrameCountAsync(1));
        Assert.Equal((ushort)10010, PacketId(correction));
        Assert.Equal((ushort)108, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(20)));
        Assert.Equal((ushort)10, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(24)));
        Assert.Equal((ushort)10, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(26)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(correction.AsSpan(22)));
    }

    [Fact]
    public async Task NativeJump_RejectsForeignIdentityWithoutMoving()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        GameAction action = new(alice.Session.Ownership.CharacterId + 1, 16, 0, 100,
            GameJumpAction.JumpAction, 0, 0, 0, 0, 0, 0, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            alice.Session.ProcessJumpAsync(new GameJumpAction(action), TestContext.Current.CancellationToken).AsTask());
        Assert.Empty(alice.AvailableFrames());
    }

    [Fact]
    public async Task Whisper_UsesCanonicalNameAcrossMapsWithoutPublishingToOtherPlayers()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bob = await world.AddAsync(1, "Bobby", SecondMap);
        ClientSession carol = await world.AddAsync(2, "Carol", SecondMap);
        GameInboundChat request = new(GameInboundChat.WhisperChannel, "bObBy", "private message");
        GameChatPacket expected = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "Bobby", request);

        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);

        await alice.ExpectPacketAsync(expected);
        await bob.ExpectPacketAsync(expected);
        Assert.DoesNotContain(carol.AvailableFrames(), frame => FrameMatches(frame, expected));

        Assert.Null(world.Visibility.PublishChat(alice.Session,
            new GameInboundChat(GameInboundChat.WhisperChannel, "Missing", "private")));
        Assert.Null(world.Visibility.PublishChat(alice.Session,
            new GameInboundChat(GameInboundChat.WhisperChannel, "CarolOffline", "private")));
    }

    [Fact]
    public async Task RemovedOrReplacedRecipient_IsNeverUsedForWhisperDelivery()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession removed = await world.AddAsync(1, "Bobby", SecondMap);

        await removed.Session.DisposeAsync();
        Assert.Null(world.Visibility.PublishChat(alice.Session,
            new GameInboundChat(GameInboundChat.WhisperChannel, "Bobby", "old owner")));
        Assert.Throws<InvalidDataException>(() => world.Visibility.PublishChat(removed.Session,
            new GameInboundChat(GameInboundChat.LocalChannel, "ALL", "stale")));

        // Delayed teardown of the old owner must not erase the new name registration.
        ClientSession replacement = await world.AddAsync(3, "Bobby", SecondMap);
        world.Visibility.Leave(removed.Session);
        GameInboundChat request = new(GameInboundChat.WhisperChannel, "bobby", "new owner");
        GameChatPacket expected = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "Bobby", request);

        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await replacement.ExpectPacketAsync(expected);
    }

    [Fact]
    public async Task DuplicateOnlineName_FailsClosedWithoutReplacingExistingRecipient()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.AddAsync(1, "ALICE", SecondMap));

        ClientSession bob = await world.AddAsync(2, "Bobby", SecondMap);
        GameInboundChat request = new(GameInboundChat.WhisperChannel, "alice", "still online");
        GameChatPacket expected = WorldChatPacketFactory.Create(bob.Session.Ownership.CharacterId, "Bobby", "Alice", request);
        await bob.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await alice.ExpectPacketAsync(expected);
    }

    [Fact]
    public async Task RoleWord_IsEmittedImmediatelyAfterSpawnAndRemovedOnLeave()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        GameInboundChat request = new(GameInboundChat.RoleWordChannel, "ALL", "looking for team");
        GameChatPacket expectedWord = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "ALL", request);
        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await alice.ExpectPacketAsync(expectedWord);

        ClientSession bob = await world.AddAsync(1, "Bobby", FirstMap);
        IReadOnlyList<byte[]> firstFrames = await bob.WaitForFrameCountAsync(2);
        Assert.Equal((ushort)10014, PacketId(firstFrames[0]));
        Assert.Equal(GameChatPacket.PacketIdentifier, PacketId(firstFrames[1]));
        Assert.True(FrameMatches(firstFrames[1], expectedWord));

        await alice.Session.DisposeAsync();
        ClientSession carol = await world.AddAsync(2, "Carol", FirstMap);
        IReadOnlyList<byte[]> newObserver = await carol.WaitForFrameCountAsync(1);
        Assert.All(newObserver, frame => Assert.False(FrameMatches(frame, expectedWord)));
    }

    [Fact]
    public async Task UnsupportedChannelsAndUnaddressedWhispers_ConsumeIngressBudget()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);

        for (int index = 0; index < WorldChatRateLimiter.MessagesPerSecond - 1; index++)
        {
            await alice.Session.ProcessChatAsync(new GameInboundChat(0x835, "ALL", "ignored"),
                TestContext.Current.CancellationToken);
        }

        await alice.Session.ProcessChatAsync(new GameInboundChat(GameInboundChat.WhisperChannel, "ALL", "ignored"),
            TestContext.Current.CancellationToken);

        using CancellationTokenSource cancellation = new();
        Task pending = alice.Session.ProcessChatAsync(new GameInboundChat(0x835, "ALL", "fifth"), cancellation.Token).AsTask();
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task SlowRecipient_DoesNotBlockMapFanoutOrDisposeTheSender()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession slow = await world.AddAsync(1, "Bobby", FirstMap);

        // Flush the admission spawn before blocking further peer writes.
        await slow.WaitForFrameCountAsync(1);
        slow.Transport.BlockSends();

        GameInboundChat request = new(GameInboundChat.LocalChannel, "ALL", "bounded");
        for (int index = 0; index < 300 && !slow.Session.IsStopping; index++)
        {
            world.Visibility.PublishChat(alice.Session, request);
        }

        Assert.True(slow.Session.IsStopping);
        Assert.False(alice.Session.IsStopping);
        await slow.Session.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        Assert.Equal(1, slow.Transport.DisposeCount);
        Assert.False(alice.Session.IsStopping);
    }

    private static ushort PacketId(ReadOnlySpan<byte> frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]);

    private static bool FrameMatches(ReadOnlySpan<byte> frame, GameChatPacket packet)
    {
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, expected);
        return frame.SequenceEqual(expected);
    }

    private sealed class ChatWorld : IAsyncDisposable
    {
        private readonly WorldMapContent _first = CreateWideMap();
        private readonly WorldMapContent _second = WorldMapTestContent.Create(SecondMap, 1016, 0);
        private readonly CharacterPresenceDirectory _presence = new();
        private readonly List<ClientSession> _clients = [];
        private readonly WorldRuntime _runtime;

        public ChatWorld()
        {
            WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(_first, _second);
            _runtime = new WorldRuntime(maps, new WorldExecutionOptions(2, 8, 4, 16, 16));
            _runtime.Start();
        }

        public WorldVisibilityService Visibility { get; } = new(NullLogger<WorldVisibilityService>.Instance);

        public async Task<ClientSession> AddAsync(uint index, string name, uint mapId, ushort x = 0, ushort y = 0)
        {
            FakeGameTransportConnection transport = new();
            GameConnectionSession connection = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
            GameClientTestPeer? client = null;
            try
            {
                using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
                client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);
                await connection.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);
                await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);
                transport.QueueReceive(client.EncryptedKeyExchangeResponse);
                string key = Assert.IsType<string>(
                    await connection.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
                GameSessionCipher cipher = exchange.Complete(key);
                try
                {
                    connection.CompleteHandshake(cipher);
                }
                catch
                {
                    cipher.Dispose();
                    throw;
                }

                int initialServerBytes = transport.SentBytes.Length;
                uint characterId = FirstCharacter + index;
                uint accountId = 42 + index;
                uint sessionUid = 0x10203040 + index;
                CharacterLoginProfile profile = CreateProfile(characterId, accountId, name, mapId, x, y);
                AuthenticatedGameConnection authenticated = new(accountId, name, sessionUid,
                    0x6E45, 0x0000665544332211, 5517, connection);
                ExistingCharacterGameConnection game = new(authenticated, profile, _presence.Register(characterId),
                    TestAccountSessions.CreateLease(accountId, sessionUid));
                WorldMapContent map = mapId == FirstMap ? _first : _second;
                ExistingCharacterBootstrapCompleteConnection bootstrap = new(game, map,
                    new CharacterItemSet(characterId, []), new CharacterSocialRelationSet(characterId, []),
                    new CharacterWeaponSkillSet(characterId, []), new CharacterMagicSet(characterId, []),
                    new CharacterSyndicateState(characterId, membership: null, syndicate: null), 0, 0);

                WorldSession session = await new WorldSessionAdmissionProcessor(_runtime, Visibility)
                    .ProcessAsync(bootstrap, TestContext.Current.CancellationToken);
                ClientSession owned = new(session, transport, client, initialServerBytes);
                _clients.Add(owned);
                return owned;
            }
            catch
            {
                client?.Dispose();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ClientSession client in _clients.AsEnumerable().Reverse())
            {
                await client.Session.DisposeAsync();
                client.DisposeClient();
            }
            await _runtime.DisposeAsync();
        }

        private static WorldMapContent CreateWideMap()
        {
            const int size = 64;
            MapBaseTerrain terrain = new(1015, size, size,
                Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), size * size), []);
            return new WorldMapContent(new MapDefinition(FirstMap, 1015, 0),
                new MapTerrainCollision(terrain, []));
        }

        private static CharacterLoginProfile CreateProfile(uint id, uint accountId, string name, uint mapId, ushort x, ushort y)
        {
            CharacterLoginIdentity identity = new(id, accountId, name);
            CharacterAppearance appearance = new(composite: 1003, hair: 410);
            CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
                previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
            CharacterAttributes attributes = new(10, 10, 10, 10, 0);
            CharacterVitals vitals = new(100, 0);
            CharacterEconomy economy = new(0, 0, 0);
            CharacterLocation location = new(mapId, x, y);
            return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals,
                economy, 0, 0, 0, location);
        }
    }

    private sealed class ClientSession(WorldSession session, FakeGameTransportConnection transport,
        GameClientTestPeer client, int firstSecuredOffset)
    {
        private readonly List<byte> _incomplete = [];
        private readonly List<byte[]> _frames = [];
        private int _consumed = firstSecuredOffset;

        public WorldSession Session { get; } = session;
        public FakeGameTransportConnection Transport { get; } = transport;

        public void DisposeClient() => client.Dispose();

        public IReadOnlyList<byte[]> AvailableFrames()
        {
            byte[] all = Transport.SentBytes;
            if (all.Length > _consumed)
            {
                byte[] decrypted = client.DecryptServerBytes(all.AsSpan(_consumed));
                _consumed = all.Length;
                _incomplete.AddRange(decrypted);
                ExtractFrames();
            }
            return _frames;
        }

        public async Task<IReadOnlyList<byte[]>> WaitForFrameCountAsync(int count)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (AvailableFrames().Count < count)
            {
                await Task.Delay(10, timeout.Token);
            }
            return _frames;
        }

        public async Task ExpectFrameAsync(byte[] expected)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!AvailableFrames().Any(frame => frame.AsSpan().SequenceEqual(expected)))
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public async Task ExpectPacketAsync(GameChatPacket packet)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!AvailableFrames().Any(frame => FrameMatches(frame, packet)))
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private void ExtractFrames()
        {
            while (_incomplete.Count >= WireFrameHeader.Size)
            {
                int size = _incomplete[0] | _incomplete[1] << 8;
                if (size < WireFrameHeader.Size || size > GameWireProtocol.MaximumPacketLength)
                {
                    throw new InvalidDataException("Encrypted test peer received an invalid frame length.");
                }
                int total = checked(size + GameWireProtocol.SignatureLength);
                if (_incomplete.Count < total)
                {
                    return;
                }
                byte[] bytes = _incomplete.GetRange(0, total).ToArray();
                if (!bytes.AsSpan(size, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature))
                {
                    throw new InvalidDataException("Encrypted test peer received an invalid server signature.");
                }
                _frames.Add(bytes[..size]);
                _incomplete.RemoveRange(0, total);
            }
        }
    }
}
