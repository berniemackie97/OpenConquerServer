using System.Buffers.Binary;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldJumpVisibilityTests
{
    [Fact]
    public async Task GroundJumpPreservesCachedFacingForPeerQueryAndLateObserver()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", WorldSessionTestWorld.FirstMap, 2, 2);
        WorldSessionTestClient bobby = await world.AddAsync(1, "Bobby", WorldSessionTestWorld.FirstMap, 5, 2);
        uint characterId = alice.Session.Ownership.CharacterId;

        await alice.Session.ProcessAvatarActionAsync(new GameAvatarAction(
            characterId, GameAvatarAction.PoseAction, 5, 150, 101), cancellationToken);
        GameAction request = new(characterId, ((uint)2 << 16) | 3, 0, 102,
            GameJumpAction.JumpAction, 0, 2, 2, WorldSessionTestWorld.FirstMap, uint.MaxValue, 0, 0);
        await alice.Session.ProcessJumpAsync(new GameJumpAction(request), cancellationToken);

        WorldCharacterSnapshot position = Assert.IsType<WorldCharacterSnapshot>(
            await world.Runtime.ReadCharacterAsync(alice.Session.Ownership, cancellationToken));
        Assert.Equal(((ushort)3, (ushort)2), (position.X, position.Y));
        IReadOnlyList<byte[]> observerFrames = await bobby.WaitForFrameCountAsync(3);
        byte[] relay = Assert.Single(observerFrames, static frame =>
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)) == GameAction.PacketIdentifier &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == GameJumpAction.JumpAction);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(relay.AsSpan(22)));
        Assert.Equal(((uint)2 << 16) | 3, BinaryPrimitives.ReadUInt32LittleEndian(relay.AsSpan(8)));

        int beforeQuery = observerFrames.Count;
        world.Visibility.Query(bobby.Session, characterId);
        IReadOnlyList<byte[]> queried = await bobby.WaitForFrameCountAsync(beforeQuery + 1);
        AssertSpawn(queried[beforeQuery], characterId, 3, 2, 5);

        WorldSessionTestClient carol = await world.AddAsync(2, "Carol", WorldSessionTestWorld.FirstMap, 6, 2);
        IReadOnlyList<byte[]> newObserverFrames = await carol.WaitForFrameCountAsync(2);
        byte[] newSpawn = Assert.Single(newObserverFrames, frame =>
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)) == 10014 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)) == characterId);
        AssertSpawn(newSpawn, characterId, 3, 2, 5);
        Assert.False(alice.Session.IsStopping);
        Assert.False(bobby.Session.IsStopping);
        Assert.False(carol.Session.IsStopping);
    }

    private static void AssertSpawn(ReadOnlySpan<byte> frame, uint entityId, ushort x, ushort y, byte direction)
    {
        Assert.Equal((ushort)10014, BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]));
        Assert.Equal(entityId, BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]));
        Assert.Equal(x, BinaryPrimitives.ReadUInt16LittleEndian(frame[0x56..]));
        Assert.Equal(y, BinaryPrimitives.ReadUInt16LittleEndian(frame[0x58..]));
        Assert.Equal(direction, frame[0x5A]);
        Assert.Equal((uint)GameAvatarAction.StandingPose, BinaryPrimitives.ReadUInt32LittleEndian(frame[0x5B..]));
    }
}
