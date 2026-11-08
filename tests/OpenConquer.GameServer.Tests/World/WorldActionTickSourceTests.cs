using System.Buffers.Binary;
using OpenConquer.GameServer.Time;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.World;

public sealed class WorldActionTickSourceTests
{
    [Theory]
    [InlineData(0xFFFF_FFFEu)]
    [InlineData(0u)]
    public async Task PositionCorrection_UsesInjectedTick(uint tick)
    {
        MutableTickSource ticks = new(tick);
        await using WorldSessionTestWorld world = new(tickSource: ticks);
        WorldSessionTestClient player = await world.AddAsync(0, "Alice", WorldSessionTestWorld.FirstMap, x: 2, y: 1);
        uint characterId = player.Session.Ownership.CharacterId;
        GamePositionReport divergent = new(characterId, 40, 40, GamePositionReport.IdleReportAction);

        for (int index = 0; index < 3; index++)
        {
            await player.Session.ProcessPositionReportAsync(divergent, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<byte[]> frames = await player.WaitForFrameCountAsync(1);
        byte[] correction = Assert.Single(frames, frame => IsAction(frame, characterId, 108));
        Assert.Equal(tick, ReadTick(correction));
    }

    [Theory]
    [InlineData(0xFFFF_FFFFu)]
    [InlineData(0u)]
    public async Task ConnectedPortal_UsesInjectedTickForDepartureAndArrival(uint tick)
    {
        MutableTickSource ticks = new(tick);
        await using WorldSessionTestWorld world = new(portals: true, tickSource: ticks);
        WorldSessionTestClient traveller = await world.AddAsync(0, "Alice", WorldSessionTestWorld.FirstMap, 2, 1);
        WorldSessionTestClient viewer = await world.AddAsync(1, "Bobby", WorldSessionTestWorld.FirstMap, 4, 1);
        uint characterId = traveller.Session.Ownership.CharacterId;

        await traveller.Session.ProcessPortalAsync(new GamePortalRequest(characterId, 2, 1),
            TestContext.Current.CancellationToken);

        IReadOnlyList<byte[]> ownerFrames = await traveller.WaitForFrameCountAsync(3);
        byte[] arrival = Assert.Single(ownerFrames, frame => IsAction(frame, characterId, GameAction.PortalArrivalAction));
        Assert.Equal(tick, ReadTick(arrival));

        IReadOnlyList<byte[]> viewerFrames = await viewer.WaitForFrameCountAsync(2);
        byte[] removal = Assert.Single(viewerFrames, frame => IsAction(frame, characterId, 135));
        Assert.Equal(tick, ReadTick(removal));
    }

    [Fact]
    public async Task VisibilityRemoval_UsesCurrentTickAcrossUnsignedWrap()
    {
        MutableTickSource ticks = new(0xFFFF_FFFFu);
        await using WorldSessionTestWorld world = new(tickSource: ticks);
        WorldSessionTestClient first = await world.AddAsync(0, "Alice", WorldSessionTestWorld.FirstMap, 2, 1);
        WorldSessionTestClient second = await world.AddAsync(1, "Bobby", WorldSessionTestWorld.FirstMap, 4, 1);
        WorldSessionTestClient viewer = await world.AddAsync(2, "Carol", WorldSessionTestWorld.FirstMap, 3, 1);

        uint firstId = first.Session.Ownership.CharacterId;
        uint secondId = second.Session.Ownership.CharacterId;
        await first.Session.DisposeAsync();

        await viewer.WaitForFrameCountAsync(3);
        ticks.CurrentTick = 0;
        await second.Session.DisposeAsync();

        IReadOnlyList<byte[]> frames = await viewer.WaitForFrameCountAsync(4);
        byte[] firstRemoval = Assert.Single(frames, frame => IsAction(frame, firstId, 135));
        byte[] secondRemoval = Assert.Single(frames, frame => IsAction(frame, secondId, 135));
        Assert.Equal(uint.MaxValue, ReadTick(firstRemoval));
        Assert.Equal(0u, ReadTick(secondRemoval));
    }

    private static bool IsAction(byte[] frame, uint characterId, ushort action) =>
        frame.Length >= 24 &&
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)) == GameAction.PacketIdentifier &&
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)) == characterId &&
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20)) == action;

    private static uint ReadTick(byte[] frame) => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(16));

    private sealed class MutableTickSource(uint tick) : IGameTickSource
    {
        public uint CurrentTick { get; set; } = tick;
    }
}
