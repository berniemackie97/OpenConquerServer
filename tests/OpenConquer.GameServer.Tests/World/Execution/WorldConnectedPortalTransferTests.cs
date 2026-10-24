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

namespace OpenConquer.GameServer.Tests.World.Execution;

/// <summary>
/// Connected 5517 action-85 transfers over the encrypted session transport: durable location commit,
/// native 86/1110 arrival order, visibility handoff and failure containment.
/// </summary>
public sealed class WorldConnectedPortalTransferTests
{
    private const uint FirstMap = WorldSessionTestWorld.FirstMap;
    private const uint SecondMap = WorldSessionTestWorld.SecondMap;

    [Fact]
    public async Task ConnectedPortal_Emits86Then1110BeforeDestinationSpawnsAndRebindsVisibility()
    {
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 4, 1);
        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", SecondMap, 1, 2);
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
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        WorldSessionTestClient carol = await world.AddAsync(1, "Carol", SecondMap, 1, 2);
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
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient traveller = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
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
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
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
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        WorldCharacterOwnership owner = alice.Session.Ownership;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            alice.Session.ProcessPortalAsync(new GamePortalRequest(owner.CharacterId, 44, 44),
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(owner, alice.Session.Ownership);
        Assert.Equal(9, world.Saved(42, owner.CharacterId).Revision);
        Assert.True(world.Runtime.IsRunning);
    }

    [Theory]
    [InlineData(PortalPersistenceFault.Conflict, typeof(InvalidOperationException))]
    [InlineData(PortalPersistenceFault.CommittedButUnknown, typeof(IOException))]
    [InlineData(PortalPersistenceFault.WrongReceipt, typeof(InvalidOperationException))]
    public async Task ConnectedPortal_RefusedOrUncertainCommitClosesOnlyTheTraveller(
        PortalPersistenceFault fault, Type expectedException)
    {
        await using WorldSessionTestWorld world = new(portals: true) { PersistenceFault = fault };
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", FirstMap, 4, 1);
        WorldCharacterOwnership source = alice.Session.Ownership;

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            alice.Session.ProcessPortalAsync(new GamePortalRequest(source.CharacterId, 2, 1),
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(expectedException, failure.GetType());
        // The connection worker disposes a session whose packet handler throws.
        await alice.Session.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // The realm keeps running. The durable row keeps whichever outcome the fenced CAS produced,
        // and no in-memory owner of the traveller remains on either map.
        Assert.True(world.Runtime.IsRunning);
        Assert.Equal(fault == PortalPersistenceFault.Conflict ? 9 : 10,
            world.Saved(42, source.CharacterId).Revision);
        Assert.Null(await world.Runtime.ReadCharacterAsync(source, TestContext.Current.CancellationToken));
        Assert.Contains(await bobby.WaitForFrameCountAsync(2), frame => PacketId(frame) == 10010 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == 135);
        Assert.False(bobby.Session.IsStopping);
        Assert.NotNull(await world.Runtime.TryWalkCharacterAsync(bobby.Session.Ownership, 6,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectedPortal_WorldShutdownAfterDurableCommitCannotPublishDestination()
    {
        await using WorldSessionTestWorld world = new(portals: true);
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", FirstMap, 2, 1);
        uint characterId = alice.Session.Ownership.CharacterId;
        TaskCompletionSource commitReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        world.CommitReached = commitReached;
        world.ReleaseCommit = releaseCommit;

        Task transfer = alice.Session.ProcessPortalAsync(new GamePortalRequest(characterId, 2, 1),
            TestContext.Current.CancellationToken).AsTask();
        try
        {
            await commitReached.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await world.Runtime.StopPartitionExecutionAsync().WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseCommit.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => transfer);
        Assert.False(world.Runtime.IsRunning);
        Assert.True(alice.Session.IsStopping);
        Assert.Equal(10, world.Saved(42, characterId).Revision);
        await alice.Session.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static ushort PacketId(ReadOnlySpan<byte> frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]);

    private static bool FrameMatches(ReadOnlySpan<byte> frame, GameChatPacket packet)
    {
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, expected);
        return frame.SequenceEqual(expected);
    }
}
