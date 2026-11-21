using OpenConquer.GameServer.World.Execution;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldMovementPacingTests
{
    [Fact]
    public async Task JumpDuringWalkAndWalkAfterLanding_AreNotRefusedByEachOther()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WorldSessionTestWorld world = new();
        WorldSessionTestClient alice = await world.AddAsync(0, "Alice", WorldSessionTestWorld.FirstMap);
        uint characterId = alice.Session.Ownership.CharacterId;

        // The native client sends a jump while its previous step is still animating.
        await alice.Session.ProcessWalkAsync(new GameWalkPacket(6, characterId, 0, 100, 0), cancellationToken);
        WorldCharacterSnapshot afterWalk = await ReadAsync(world, alice, cancellationToken);

        ushort targetX = (ushort)(afterWalk.X + 12);
        GameAction request = new(characterId, ((uint)afterWalk.Y << 16) | targetX, 0, 101,
            GameJumpAction.JumpAction, 0, 0, 0, 0, 0, 0, 0);
        await alice.Session.ProcessJumpAsync(new GameJumpAction(request), cancellationToken);
        WorldCharacterSnapshot afterJump = await ReadAsync(world, alice, cancellationToken);
        Assert.Equal((targetX, afterWalk.Y), (afterJump.X, afterJump.Y));

        // Clear only the 125 ms walk-to-walk interval; the old shared budget charged 12 x 125 ms after the jump.
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        await alice.Session.ProcessWalkAsync(new GameWalkPacket(6, characterId, 0, 102, 0), cancellationToken);
        WorldCharacterSnapshot afterSecondWalk = await ReadAsync(world, alice, cancellationToken);
        Assert.NotEqual((afterJump.X, afterJump.Y), (afterSecondWalk.X, afterSecondWalk.Y));
        Assert.False(alice.Session.IsStopping);
    }

    private static async Task<WorldCharacterSnapshot> ReadAsync(WorldSessionTestWorld world,
        WorldSessionTestClient client, CancellationToken cancellationToken) =>
        Assert.IsType<WorldCharacterSnapshot>(
            await world.Runtime.ReadCharacterAsync(client.Session.Ownership, cancellationToken));
}
