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
using OpenConquer.GameServer.Tests.World;
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
    private const uint FirstMap = WorldSessionTestWorld.FirstMap;
    private const uint SecondMap = WorldSessionTestWorld.SecondMap;

    [Fact]
    public async Task NativeTradeQuit_IsIdempotentAndCannotImpersonateAnotherPlayer()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        WorldSessionTestClient observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
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

        WorldSessionTestClient late = await world.AddAsync(2, "Carol", FirstMap, 8, 2);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        WorldSessionTestClient observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
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

        WorldSessionTestClient late = await world.AddAsync(2, "Carol", FirstMap, 8, 2);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
        IReadOnlyList<byte[]> ownFrames = await corpse.WaitForFrameCountAsync(1);
        Assert.Contains(ownFrames, frame => PacketId(frame) == 10017 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)) == 25 &&
            BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(16)) == 1UL << 5);

        WorldSessionTestClient observer = await world.AddAsync(1, "Bobby", FirstMap, 5, 2);
        IReadOnlyList<byte[]> other = await observer.WaitForFrameCountAsync(1);
        byte[] spawn = Assert.Single(other, frame => PacketId(frame) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == corpse.Session.Ownership.CharacterId);
        Assert.Equal(1UL << 5, BinaryPrimitives.ReadUInt64LittleEndian(spawn.AsSpan(0x16)));
        Assert.Equal(0x14Bu, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(0x5B)));
    }

    [Fact]
    public async Task GhostRequest_CannotBypassCorpseDelay_AndDoesNotEchoAction145()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient corpse = await world.AddAsync(0, "Alice", FirstMap, 2, 2, life: 0);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient bob = await world.AddAsync(1, "Bobby", FirstMap);
        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", SecondMap);
        WorldSessionTestClient dave = await world.AddAsync(3, "Dave", FirstMap, 40, 40);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 1, 1);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        // One eastward ground step brings Bob exactly to the native radius 18.
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 19, 0);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        // Keep Bobby visible without occupying Alice's landing tile (16, 0).
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 16, 1);
        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", SecondMap);
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
        await using WorldSessionTestWorld world = new();
        // The two players are initially visible at exactly the native radius.
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 18, 1);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 0, 1);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 12, 0);

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
        await using WorldSessionTestWorld world = new();
        // A blocked west step at x=0, or an in-range jump off the map
        // from x=63, exercises the authoritative partition rejection path.
        ushort startX = jump ? (ushort)63 : (ushort)0;
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, startX, 0);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 10, 10);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 10, 10);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 12, 0);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        GameAction action = new(alice.Session.Ownership.CharacterId + 1, 16, 0, 100,
            GameJumpAction.JumpAction, 0, 0, 0, 0, 0, 0, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            alice.Session.ProcessJumpAsync(new GameJumpAction(action), TestContext.Current.CancellationToken).AsTask());
        Assert.Empty(alice.AvailableFrames());
    }

    [Fact]
    public async Task Whisper_UsesCanonicalNameAcrossMapsWithoutPublishingToOtherPlayers()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient bob = await world.AddAsync(1, "Bobby", SecondMap);
        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", SecondMap);
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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient removed = await world.AddAsync(1, "Bobby", SecondMap);

        await removed.Session.DisposeAsync();
        Assert.Null(world.Visibility.PublishChat(alice.Session,
            new GameInboundChat(GameInboundChat.WhisperChannel, "Bobby", "old owner")));
        Assert.Throws<InvalidDataException>(() => world.Visibility.PublishChat(removed.Session,
            new GameInboundChat(GameInboundChat.LocalChannel, "ALL", "stale")));

        // Delayed teardown of the old owner must not erase the new name registration.
        WorldSessionTestClient replacement = await world.AddAsync(3, "Bobby", SecondMap);
        world.Visibility.Leave(removed.Session);
        GameInboundChat request = new(GameInboundChat.WhisperChannel, "bobby", "new owner");
        GameChatPacket expected = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "Bobby", request);

        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await replacement.ExpectPacketAsync(expected);
    }

    [Fact]
    public async Task DuplicateOnlineName_FailsClosedWithoutReplacingExistingRecipient()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.AddAsync(1, "ALICE", SecondMap));

        WorldSessionTestClient bob = await world.AddAsync(2, "Bobby", SecondMap);
        GameInboundChat request = new(GameInboundChat.WhisperChannel, "alice", "still online");
        GameChatPacket expected = WorldChatPacketFactory.Create(bob.Session.Ownership.CharacterId, "Bobby", "Alice", request);
        await bob.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await alice.ExpectPacketAsync(expected);
    }

    [Fact]
    public async Task RoleWord_IsEmittedImmediatelyAfterSpawnAndRemovedOnLeave()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        GameInboundChat request = new(GameInboundChat.RoleWordChannel, "ALL", "looking for team");
        GameChatPacket expectedWord = WorldChatPacketFactory.Create(alice.Session.Ownership.CharacterId, "Alice", "ALL", request);
        await alice.Session.ProcessChatAsync(request, TestContext.Current.CancellationToken);
        await alice.ExpectPacketAsync(expectedWord);

        WorldSessionTestClient bob = await world.AddAsync(1, "Bobby", FirstMap);
        IReadOnlyList<byte[]> firstFrames = await bob.WaitForFrameCountAsync(2);
        Assert.Equal((ushort)10014, PacketId(firstFrames[0]));
        Assert.Equal(GameChatPacket.PacketIdentifier, PacketId(firstFrames[1]));
        Assert.True(FrameMatches(firstFrames[1], expectedWord));

        await alice.Session.DisposeAsync();
        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", FirstMap);
        IReadOnlyList<byte[]> newObserver = await carol.WaitForFrameCountAsync(1);
        Assert.All(newObserver, frame => Assert.False(FrameMatches(frame, expectedWord)));
    }

    [Fact]
    public async Task UnsupportedChannelsAndUnaddressedWhispers_ConsumeIngressBudget()
    {
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);

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
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap);
        WorldSessionTestClient slow = await world.AddAsync(1, "Bobby", FirstMap);

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

}
