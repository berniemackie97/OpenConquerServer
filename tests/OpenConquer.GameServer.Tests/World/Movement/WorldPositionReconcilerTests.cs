using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldPositionReconcilerTests
{
    [Fact]
    public void MatchingInitialTileNeverTriggersCorrection()
    {
        WorldPositionReconciler state = new(100, 200);
        for (int i = 0; i < 50; i++)
        {
            Assert.False(state.ShouldCorrect(100, 200, i));
        }
    }

    [Fact]
    public void APreviouslyAuthorizedTileIsAnInFlightReportEvenAfterLongMovement()
    {
        WorldPositionReconciler state = new(100, 100);
        for (ushort x = 101; x < 155; x++)
        {
            state.RecordAuthorized(x, 100);
        }

        Assert.False(state.ShouldCorrect(100, 100, 0));
        Assert.False(state.ShouldCorrect(100, 100, 1));
        Assert.False(state.ShouldCorrect(100, 100, 2));
    }

    [Fact]
    public void ThreeConsecutiveNeverAuthorizedReportsTriggerOneCorrection()
    {
        WorldPositionReconciler state = new(100, 100);
        Assert.False(state.ShouldCorrect(999, 999, 10));
        Assert.False(state.ShouldCorrect(999, 999, 11));
        Assert.True(state.ShouldCorrect(999, 999, 12));
        Assert.False(state.ShouldCorrect(999, 999, 13));
    }

    [Fact]
    public void AMatchingReportResetsTheDivergenceRun()
    {
        WorldPositionReconciler state = new(100, 100);
        Assert.False(state.ShouldCorrect(101, 101, 0));
        Assert.False(state.ShouldCorrect(102, 102, 1));
        Assert.False(state.ShouldCorrect(100, 100, 2));
        Assert.False(state.ShouldCorrect(101, 101, 3));
        Assert.False(state.ShouldCorrect(101, 101, 4));
        Assert.True(state.ShouldCorrect(101, 101, 5));
    }

    [Fact]
    public void CorrectionRequiresAnotherFullRunAndServerClockCooldown()
    {
        WorldPositionReconciler state = new(100, 100);
        Assert.False(state.ShouldCorrect(200, 200, 1));
        Assert.False(state.ShouldCorrect(200, 200, 2));
        Assert.True(state.ShouldCorrect(200, 200, 3));
        Assert.False(state.ShouldCorrect(200, 200, 4));
        Assert.False(state.ShouldCorrect(200, 200, 5));
        Assert.False(state.ShouldCorrect(200, 200, 6));
        Assert.True(state.ShouldCorrect(200, 200, 3 + Stopwatch.Frequency));
    }

    [Fact]
    public void RingEvictsOldTilesWithoutUnboundedAllocation()
    {
        WorldPositionReconciler state = new(100, 100);
        for (ushort x = 101; x < 167; x++)
        {
            state.RecordAuthorized(x, 100);
        }
        Assert.False(state.ShouldCorrect(100, 100, 0));
        Assert.False(state.ShouldCorrect(100, 100, 1));
        Assert.True(state.ShouldCorrect(100, 100, 2));
        Assert.False(state.ShouldCorrect(166, 100, 3));
    }
}
