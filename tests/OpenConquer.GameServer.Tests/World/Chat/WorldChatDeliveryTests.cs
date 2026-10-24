using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
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
    public async Task NativeTradeQuit_IsIdempotentAndCannotImpersonateAnotherPlayer()
    {
        await using ChatWorld world = new();
        ClientSession corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        await corpse.WaitForFrameCountAsync(1);
        GameTradeQuitRequest quit = new(corpse.Session.Ownership.CharacterId);

        corpse.Session.ProcessTradeQuit(quit);
        corpse.Session.ProcessTradeQuit(quit);

        Assert.False(corpse.Session.IsStopping);
        Assert.Single(corpse.AvailableFrames()); // Only the initial dead-status update.
        Assert.Throws<InvalidDataException>(() =>
            corpse.Session.ProcessTradeQuit(new GameTradeQuitRequest(corpse.Session.Ownership.CharacterId + 1)));
        Assert.False(corpse.Session.IsStopping);
    }

    [Fact]
    public async Task CorpseAvatarSpoof_CannotChangeLateObserverPoseOrReceiveAcknowledgement()
    {
        await using ChatWorld world = new();
        ClientSession corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        ClientSession observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
        await corpse.WaitForFrameCountAsync(2);
        await observer.WaitForFrameCountAsync(1);

        GameAvatarAction pose = new(corpse.Session.Ownership.CharacterId,
            GameAvatarAction.PoseAction, 4, GameAvatarAction.StandingPose, 100);
        GameAvatarAction facing = new(corpse.Session.Ownership.CharacterId,
            GameAvatarAction.DirectionAction, 7, 0, 101);
        await corpse.Session.ProcessAvatarActionAsync(pose, TestContext.Current.CancellationToken);
        await corpse.Session.ProcessAvatarActionAsync(facing, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(corpse.AvailableFrames(), frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) is
                (GameAvatarAction.PoseAction or GameAvatarAction.DirectionAction));
        Assert.DoesNotContain(observer.AvailableFrames(), frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) is
                (GameAvatarAction.PoseAction or GameAvatarAction.DirectionAction));

        ClientSession late = await world.AddAsync(2, "Carol", FirstMap, 8, 2);
        IReadOnlyList<byte[]> frames = await late.WaitForFrameCountAsync(2);
        byte[] spawn = Assert.Single(frames, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == corpse.Session.Ownership.CharacterId);
        Assert.Equal(0x14Bu, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(0x5B)));
        Assert.Equal((byte)0, spawn[0x5A]);
        Assert.Equal(1UL << 5, BinaryPrimitives.ReadUInt64LittleEndian(spawn.AsSpan(0x16)));
        Assert.False(corpse.Session.IsStopping);
    }

    [Fact]
    public async Task GhostTransition_PublishesNativeAttributesAndUpdatesLateObserverSpawn()
    {
        await using ChatWorld world = new();
        ClientSession corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        ClientSession observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
        await corpse.WaitForFrameCountAsync(2);
        await observer.WaitForFrameCountAsync(1);

        // Integration exercises the real monotonic clock, while partition tests
        // verify the exact 3000ms threshold without sleeping.
        await Task.Delay(TimeSpan.FromMilliseconds(3100), TestContext.Current.CancellationToken);
        await corpse.Session.ProcessGhostTransitionAsync(new GameGhostTransitionRequest(corpse.Session.Ownership.CharacterId),
            TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> peerFrames = await observer.WaitForFrameCountAsync(3);
        Assert.Contains(peerFrames, frame => PacketId(frame) == 10017 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)) == 25 &&
            BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(16)) == (1UL << 5 | 1UL << 10));
        Assert.Contains(peerFrames, frame => PacketId(frame) == 10017 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)) == 11 &&
            BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(16)) == 980001003u);
        Assert.DoesNotContain(corpse.AvailableFrames(), frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == 145);

        GameWalkPacket ghostWalk = new(6, corpse.Session.Ownership.CharacterId, 0, 100, 0);
        byte[] expectedWalk = new byte[GameWireFrameEncoder.GetFrameLength(ghostWalk)];
        GameWireFrameEncoder.WriteFrame(ghostWalk, expectedWalk);
        await corpse.Session.ProcessWalkAsync(ghostWalk, TestContext.Current.CancellationToken);
        await observer.ExpectFrameAsync(expectedWalk);

        ClientSession late = await world.AddAsync(2, "Carol", FirstMap, 8, 2);
        IReadOnlyList<byte[]> lateFrames = await late.WaitForFrameCountAsync(2);
        byte[] spawn = Assert.Single(lateFrames, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == corpse.Session.Ownership.CharacterId);
        Assert.Equal(980001003u, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(4)));
        Assert.Equal(1UL << 5 | 1UL << 10, BinaryPrimitives.ReadUInt64LittleEndian(spawn.AsSpan(0x16)));
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(0x5B)));

        // Ghosts (unlike corpses) remain authorized to change facing.
        GameAvatarAction facing = new(corpse.Session.Ownership.CharacterId,
            GameAvatarAction.DirectionAction, 3, 0, 101);
        GameActionPacket facingEcho = facing.CreateAcknowledgement();
        byte[] expectedFacing = new byte[GameWireFrameEncoder.GetFrameLength(facingEcho)];
        GameWireFrameEncoder.WriteFrame(facingEcho, expectedFacing);
        await corpse.Session.ProcessAvatarActionAsync(facing, TestContext.Current.CancellationToken);
        await observer.ExpectFrameAsync(expectedFacing);
    }

    [Fact]
    public async Task CorpseAdmission_SendsStatusToLocalClientAndPeerSpawnCarriesDeadMaskAndPose()
    {
        await using ChatWorld world = new();
        ClientSession corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        IReadOnlyList<byte[]> ownFrames = await corpse.WaitForFrameCountAsync(1);
        Assert.Contains(ownFrames, frame => PacketId(frame) == 10017 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)) == 25 &&
            BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(16)) == 1UL << 5);

        ClientSession observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
        IReadOnlyList<byte[]> other = await observer.WaitForFrameCountAsync(1);
        byte[] spawn = Assert.Single(other, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == corpse.Session.Ownership.CharacterId);
        Assert.Equal(1UL << 5, BinaryPrimitives.ReadUInt64LittleEndian(spawn.AsSpan(0x16)));
        Assert.Equal(0x14Bu, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(0x5B)));
    }

    [Fact]
    public async Task GhostRequest_CannotBypassCorpseDelay_AndDoesNotEchoAction145()
    {
        await using ChatWorld world = new();
        ClientSession corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        await corpse.WaitForFrameCountAsync(1);
        await corpse.Session.ProcessGhostTransitionAsync(new GameGhostTransitionRequest(corpse.Session.Ownership.CharacterId),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(corpse.AvailableFrames(), frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == 145);
        Assert.DoesNotContain(corpse.AvailableFrames(), frame => PacketId(frame) == 10017 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)) == 25 &&
            BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(16)) == (1UL << 5 | 1UL << 10));
        Assert.False(corpse.Session.IsStopping);
    }

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
    public async Task UnauthenticatedMountedMovement_ReceivesCorrectionWithoutPeerReplication()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 1, 1);
        await alice.WaitForFrameCountAsync(1);
        await bobby.WaitForFrameCountAsync(1);

        GameWalkPacket request = new(0xA1B2_C306, alice.Session.Ownership.CharacterId,
            GameWalkPacket.TwentyFourDirectionSelector, 100, 0xDEAD_BEEFu);
        byte[] forbiddenEcho = new byte[GameWireFrameEncoder.GetFrameLength(request)];
        GameWireFrameEncoder.WriteFrame(request, forbiddenEcho);
        await alice.Session.ProcessWalkAsync(request, TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> senderFrames = await alice.WaitForFrameCountAsync(2);
        Assert.Contains(senderFrames, frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == 108 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(24)) == 0 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(26)) == 0);
        Assert.DoesNotContain(senderFrames, frame => frame.AsSpan().SequenceEqual(forbiddenEcho));
        Assert.DoesNotContain(bobby.AvailableFrames(), frame => frame.AsSpan().SequenceEqual(forbiddenEcho));
    }

    [Fact]
    public async Task LocalSpeech_ReachesNewObserverAfterNativeGroundStep()
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        // One eastward ground step brings Bob exactly to the native radius 18.
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 19, 0);
        Assert.Empty(alice.AvailableFrames());
        Assert.Empty(bobby.AvailableFrames());

        GameWalkPacket movement = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);
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
        // Keep Bobby visible without occupying Alice's landing tile (16, 0).
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 16, 1);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SenderMailboxOverflow_DoesNotLoseCommittedMovementForHealthyObservers(bool jump)
    {
        await using ChatWorld world = new();
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 12, 0);
        await alice.WaitForFrameCountAsync(1);
        await bobby.WaitForFrameCountAsync(1);

        // Hold one outbound write in the transport, then fill the 256-packet
        // mailbox. The movement's own echo is the overflow that starts teardown.
        alice.Transport.BlockSends();
        GameWalkPacket filler = new(6, alice.Session.Ownership.CharacterId, 0, 1, 0);
        alice.Session.QueueVisibilityPacket(filler);
        await alice.Transport.SendBlocked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (int i = 0; i < 256; i++)
        {
            alice.Session.QueueVisibilityPacket(filler);
        }

        byte[] expected;
        if (jump)
        {
            GameJumpAction request = new(new GameAction(alice.Session.Ownership.CharacterId,
                6, 0, 100, GameJumpAction.JumpAction, 6, 0, 0, 0, 0, 0, 0));
            GameActionPacket acknowledgement = request.CreateAcknowledgement();
            expected = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
            GameWireFrameEncoder.WriteFrame(acknowledgement, expected);
            await alice.Session.ProcessJumpAsync(request, TestContext.Current.CancellationToken);
        }
        else
        {
            GameWalkPacket request = new(6, alice.Session.Ownership.CharacterId, 0, 100, 0);
            expected = new byte[GameWireFrameEncoder.GetFrameLength(request)];
            GameWireFrameEncoder.WriteFrame(request, expected);
            await alice.Session.ProcessWalkAsync(request, TestContext.Current.CancellationToken);
        }

        // The sender is disconnected for backpressure, but map movement was
        // already committed and the unaffected observer must receive it.
        Assert.True(alice.Session.IsStopping);
        await bobby.ExpectFrameAsync(expected);
        Assert.False(bobby.Session.IsStopping);
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

    [Fact]
    public async Task ConnectedPortal_Emits86Then1110BeforeDestinationSpawnsAndRebindsVisibility()
    {
        await using ChatWorld world = new(portals: true);
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        ClientSession bobby = await world.AddAsync(1, "Bobby", FirstMap, 4, 1);
        ClientSession carol = await world.AddAsync(2, "Carol", SecondMap, 1, 2);
        await alice.WaitForFrameCountAsync(1);
        await bobby.WaitForFrameCountAsync(1);

        WorldCharacterOwnership source = alice.Session.Ownership;
        await alice.Session.ProcessPortalAsync(new GamePortalRequest(source.CharacterId, 2, 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(SecondMap, alice.Session.Ownership.MapId);
        Assert.NotEqual(source.Generation, alice.Session.Ownership.Generation);
        Assert.Equal(1, world.Runtime.ResidentCount - 2);
        Assert.Null(await world.Runtime.ReadCharacterAsync(source, TestContext.Current.CancellationToken));
        PersistedCharacterLocation saved = world.Saved(42, source.CharacterId);
        Assert.Equal(SecondMap, saved.Location.MapId);
        Assert.Equal((ushort)1, saved.Location.X);
        Assert.Equal((ushort)1, saved.Location.Y);
        Assert.Equal(10, saved.Revision);
        Assert.NotNull(saved.LastOperationId);
        Assert.Equal(alice.Session.LocationAuthority!.Token, saved.WriterToken);

        IReadOnlyList<byte[]> outgoing = await alice.WaitForFrameCountAsync(4);
        Assert.Equal((ushort)10010, PacketId(outgoing[1]));
        Assert.Equal(GameAction.PortalArrivalAction,
            BinaryPrimitives.ReadUInt16LittleEndian(outgoing[1].AsSpan(20)));
        Assert.Equal(1016u, BinaryPrimitives.ReadUInt32LittleEndian(outgoing[1].AsSpan(8)));
        Assert.Equal((ushort)1110, PacketId(outgoing[2]));
        Assert.Equal(SecondMap, BinaryPrimitives.ReadUInt32LittleEndian(outgoing[2].AsSpan(4)));
        Assert.Equal((ushort)10014, PacketId(outgoing[3]));

        IReadOnlyList<byte[]> oldView = await bobby.WaitForFrameCountAsync(2);
        Assert.Contains(oldView, frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == 135);
        IReadOnlyList<byte[]> newView = await carol.WaitForFrameCountAsync(1);
        Assert.Contains(newView, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == source.CharacterId);
        Assert.False(alice.Session.IsStopping);
        await alice.Session.ProcessChatAsync(new GameInboundChat(GameInboundChat.LocalChannel,
            "ALL", "arrived"), TestContext.Current.CancellationToken);
        GameChatPacket chat = WorldChatPacketFactory.Create(source.CharacterId, "Alice", "ALL",
            new GameInboundChat(GameInboundChat.LocalChannel, "ALL", "arrived"));
        await carol.ExpectPacketAsync(chat);
        Assert.DoesNotContain(bobby.AvailableFrames(), frame => FrameMatches(frame, chat));
    }

    [Fact]
    public async Task ConnectedPortal_PreservesFacingInArrivalAndDestinationPeerSpawn()
    {
        await using ChatWorld world = new(portals: true);
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        ClientSession carol = await world.AddAsync(1, "Carol", SecondMap, 1, 2);
        uint characterId = alice.Session.Ownership.CharacterId;

        await alice.Session.ProcessAvatarActionAsync(new GameAvatarAction(
            characterId, GameAvatarAction.DirectionAction, 3, 0, 101),
            TestContext.Current.CancellationToken);
        await alice.Session.ProcessPortalAsync(new GamePortalRequest(characterId, 2, 1),
            TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> ownerFrames = await alice.WaitForFrameCountAsync(4);
        byte[] arrival = Assert.Single(ownerFrames, frame => PacketId(frame) == GameAction.PacketIdentifier &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == GameAction.PortalArrivalAction);
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(arrival.AsSpan(22)));

        IReadOnlyList<byte[]> peerFrames = await carol.WaitForFrameCountAsync(1);
        byte[] peerSpawn = Assert.Single(peerFrames, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == characterId);
        Assert.Equal((byte)3, peerSpawn[0x5A]);
    }

    [Fact]
    public async Task ConnectedPortal_DenseDestination_DeliversAllSpawnsWithoutExhaustingMailbox()
    {
        const int spectators = 260;
        await using ChatWorld world = new(portals: true);
        ClientSession traveller = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        for (uint index = 1; index <= spectators; index++)
        {
            await world.AddAsync(index, $"Peer{index}", SecondMap, 1, 2);
        }

        uint characterId = traveller.Session.Ownership.CharacterId;
        await traveller.Session.ProcessPortalAsync(new GamePortalRequest(characterId, 2, 1),
            TestContext.Current.CancellationToken);
        Assert.False(traveller.Session.IsStopping);
        IReadOnlyList<byte[]> frames = await traveller.WaitForFrameCountAsync(spectators + 2);
        Assert.Equal(GameAction.PortalArrivalAction, BinaryPrimitives.ReadUInt16LittleEndian(frames[0].AsSpan(20)));
        Assert.Equal((ushort)1110, PacketId(frames[1]));
        Assert.Equal(spectators, frames.Skip(2).Count(frame => PacketId(frame) == 10014));
        Assert.False(traveller.Session.IsStopping);
    }

    [Fact]
    public async Task NativeClientStateApplied_RequiresZeroFieldsAndNeverAcknowledges()
    {
        await using ChatWorld world = new(portals: true);
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        GameAction valid = new(0, 0, 0, 0, GameAction.ClientStateAppliedAction,
            0, 0, 0, 0, 0, 0, 0);
        alice.Session.ProcessClientStateApplied(valid);
        Assert.Empty(alice.AvailableFrames());

        GameAction forged = valid with { EntityId = alice.Session.Ownership.CharacterId };
        Assert.Throws<InvalidDataException>(() => alice.Session.ProcessClientStateApplied(forged));
    }

    [Fact]
    public async Task ConnectedPortal_InvalidExitCannotPersistOrMoveCharacter()
    {
        await using ChatWorld world = new(portals: true);
        ClientSession alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        WorldCharacterOwnership owner = alice.Session.Ownership;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            alice.Session.ProcessPortalAsync(new GamePortalRequest(owner.CharacterId, 44, 44),
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(owner, alice.Session.Ownership);
        Assert.Equal(9, world.Saved(42, owner.CharacterId).Revision);
        Assert.True(world.Runtime.IsRunning);
    }

    private static ushort PacketId(ReadOnlySpan<byte> frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]);

    private static bool FrameMatches(ReadOnlySpan<byte> frame, GameChatPacket packet)
    {
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, expected);
        return frame.SequenceEqual(expected);
    }

    private sealed class ChatWorld : IAsyncDisposable, ICharacterLocationWriteStore,
        ICharacterLocationObservationStore
    {
        private readonly WorldMapContent _first;
        private readonly WorldMapContent _second;
        private readonly object _persistedGate = new();
        private readonly Dictionary<(uint AccountId, uint CharacterId), PersistedCharacterLocation> _saved = [];
        private readonly WorldConnectedPortalTransferCoordinator? _portals;
        private readonly bool _enablePortals;
        private readonly CharacterPresenceDirectory _presence = new();
        private readonly List<ClientSession> _clients = [];
        private readonly WorldRuntime _runtime;

        public ChatWorld(bool portals = false)
        {
            _enablePortals = portals;
            _first = CreateWideMap(FirstMap, 1015, portals ? [new MapTerrainExit(2, 1, 7)] : []);
            _second = portals ? CreateWideMap(SecondMap, 1016) :
                WorldMapTestContent.Create(SecondMap, 1016, 0);
            WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(_first, _second);
            _runtime = new WorldRuntime(maps, new WorldExecutionOptions(2, 8, 4, portals ? 512 : 16, portals ? 512 : 16));
            if (portals)
            {
                MapTransitionCatalog catalog = new(
                    [new MapTransitionEntry(FirstMap, 7, "release-approved",
                        new MapTransitionDestination(SecondMap, 1, 1, 1, "L"))], maps);
                _portals = new WorldConnectedPortalTransferCoordinator(_runtime, catalog,
                    this, this, Visibility);
            }
            _runtime.Start();
        }

        public WorldRuntime Runtime => _runtime;

        public PersistedCharacterLocation Saved(uint accountId, uint characterId)
        {
            lock (_persistedGate)
            {
                return _saved[(accountId, characterId)];
            }
        }

        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_persistedGate)
            {
                _saved.TryGetValue((accountId, characterId), out PersistedCharacterLocation? value);
                return ValueTask.FromResult(value);
            }
        }

        public ValueTask<CharacterLocationWriteResult> TryCommitAsync(CharacterLocationChange change,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_persistedGate)
            {
                if (!_saved.TryGetValue((change.AccountId, change.CharacterId),
                    out PersistedCharacterLocation? saved) ||
                    saved.Revision != change.ExpectedRevision ||
                    saved.WriterToken != change.WriterToken ||
                    saved.Location.MapId != change.Expected.MapId ||
                    saved.Location.X != change.Expected.X ||
                    saved.Location.Y != change.Expected.Y)
                {
                    return ValueTask.FromResult(CharacterLocationWriteResult.Conflict);
                }

                _saved[(change.AccountId, change.CharacterId)] = new PersistedCharacterLocation(
                    change.Destination, change.ExpectedRevision + 1, change.OperationId, change.WriterToken);
                return ValueTask.FromResult(CharacterLocationWriteResult.Committed);
            }
        }

        public WorldVisibilityService Visibility { get; } = new(NullLogger<WorldVisibilityService>.Instance);

        public async Task<ClientSession> AddAsync(uint index, string name, uint mapId, ushort x = 0, ushort y = 0, ushort life = 100)
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
                CharacterLoginProfile profile = CreateProfile(characterId, accountId, name, mapId, x, y, life);
                AuthenticatedGameConnection authenticated = new(accountId, name, sessionUid,
                    0x6E45, 0x0000665544332211, 5517, connection);
                CharacterLocationWriteAuthority? authority = null;
                if (_enablePortals)
                {
                    Guid token = Guid.NewGuid();
                    PersistedCharacterLocation persisted = new(new CharacterLocation(mapId, x, y),
                        9, writerToken: token);
                    lock (_persistedGate)
                    {
                        _saved.Add((accountId, characterId), persisted);
                    }
                    authority = new CharacterLocationWriteAuthority(accountId, characterId, token, persisted);
                }
                ExistingCharacterGameConnection game = new(authenticated, profile, _presence.Register(characterId),
                    TestAccountSessions.CreateLease(accountId, sessionUid), authority);
                WorldMapContent map = mapId == FirstMap ? _first : _second;
                ExistingCharacterBootstrapCompleteConnection bootstrap = new(game, map,
                    new CharacterItemSet(characterId, []), new CharacterSocialRelationSet(characterId, []),
                    new CharacterWeaponSkillSet(characterId, []), new CharacterMagicSet(characterId, []),
                    new CharacterSyndicateState(characterId, membership: null, syndicate: null), 0, 0);

                WorldSession session = await new WorldSessionAdmissionProcessor(_runtime, Visibility,
                    portals: _portals).ProcessAsync(bootstrap, TestContext.Current.CancellationToken);
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

        private static WorldMapContent CreateWideMap(uint mapId, uint terrainId, MapTerrainExit[]? exits = null)
        {
            const int size = 64;
            MapBaseTerrain terrain = new(terrainId, size, size,
                Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), size * size), exits ?? []);
            return new WorldMapContent(new MapDefinition(mapId, terrainId, 0),
                new MapTerrainCollision(terrain, []));
        }

        private static CharacterLoginProfile CreateProfile(uint id, uint accountId, string name, uint mapId, ushort x, ushort y, ushort life)
        {
            CharacterLoginIdentity identity = new(id, accountId, name);
            CharacterAppearance appearance = new(composite: 1003, hair: 410);
            CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
                previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
            CharacterAttributes attributes = new(10, 10, 10, 10, 0);
            CharacterVitals vitals = new(life, 0);
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
