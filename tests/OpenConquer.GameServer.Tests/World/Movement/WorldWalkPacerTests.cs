using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldWalkPacerTests
{
    private static readonly long s_interval = (Stopwatch.Frequency + 7) / 8;

    [Fact]
    public void TryAccept_RequiresServerElapsedTimeNotClientReportedElapsedTime()
    {
        WorldWalkPacer pacing = new();

        Assert.True(pacing.TryAccept(100, 1000));
        Assert.False(pacing.TryAccept(500_000, 1000 + s_interval - 1));
        Assert.True(pacing.TryAccept(101, 1000 + s_interval));
    }

    [Fact]
    public void TryAccept_RejectsDuplicateAndBackwardsClientTicks()
    {
        WorldWalkPacer pacing = new();
        Assert.True(pacing.TryAccept(100, 1000));
        Assert.False(pacing.TryAccept(100, 1000 + s_interval));
        Assert.False(pacing.TryAccept(99, 1000 + s_interval));
        Assert.True(pacing.TryAccept(101, 1000 + s_interval));
    }

    [Fact]
    public void TryAccept_TwoTileMovesAreBoundedAtDoubleTheNormalServerClockInterval()
    {
        WorldWalkPacer pacing = new();
        long start = 1000;
        long extended = checked(2 * s_interval);

        Assert.True(pacing.TryAccept(10, start));
        Assert.False(pacing.TryAccept(11, start + s_interval, twoTileMove: true));
        Assert.True(pacing.TryAccept(11, start + extended, twoTileMove: true));
        Assert.False(pacing.TryAccept(12, start + extended + s_interval));
        Assert.True(pacing.TryAccept(12, start + extended * 2));
        Assert.True(pacing.TryAccept(13, start + extended * 2 + s_interval));
    }

    [Fact]
    public void JumpAndWalking_ShareClientReplayAndDistanceWeightedServerPacing()
    {
        WorldWalkPacer pacer = new();
        long start = 1000;
        Assert.True(pacer.TryAccept(100, start));
        Assert.False(pacer.TryAcceptJump(101, start + s_interval, 16));
        Assert.True(pacer.TryAcceptJump(101, start + 16 * s_interval, 16));
        Assert.False(pacer.TryAccept(102, start + 17 * s_interval));
        Assert.False(pacer.TryAcceptJump(101, start + 32 * s_interval, 1));
        Assert.True(pacer.TryAccept(102, start + 32 * s_interval));
        Assert.True(pacer.TryAcceptJump(103, start + 33 * s_interval, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void JumpPacer_RejectsInvalidDistanceWithoutChangingReplayState(int distance)
    {
        WorldWalkPacer pacer = new();
        Assert.False(pacer.TryAcceptJump(100, 1000, distance));
        Assert.True(pacer.TryAcceptJump(100, 1000, 1));
    }

    [Fact]
    public void TryAccept_HandlesUInt32TickWrapWithoutReplayingOldRequests()
    {
        WorldWalkPacer pacing = new();
        Assert.True(pacing.TryAccept(uint.MaxValue - 1, 1000));
        Assert.True(pacing.TryAccept(1, 1000 + s_interval));
        Assert.False(pacing.TryAccept(uint.MaxValue - 1, 1000 + 2 * s_interval));
    }

    [Fact]
    public void TryAccept_RejectsClockRegressionWithoutMutatingAcceptedState()
    {
        WorldWalkPacer pacing = new();
        Assert.True(pacing.TryAccept(5, 2000));
        Assert.False(pacing.TryAccept(6, 1000));
        Assert.True(pacing.TryAccept(6, 2000 + s_interval));
    }
}
