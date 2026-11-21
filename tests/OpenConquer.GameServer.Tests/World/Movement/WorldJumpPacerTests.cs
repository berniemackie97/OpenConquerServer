using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldJumpPacerTests
{
    // Three intervals of 643 ms (TQ 15.10 CUser__CheckJumpTimeStamp mean threshold).
    private static readonly long s_window = 3 * 643 * Stopwatch.Frequency / 1000;

    [Fact]
    public void TryAccept_FirstFourJumpsPassRegardlessOfSpacing()
    {
        WorldJumpPacer pacer = new();
        for (uint i = 0; i < 4; i++)
        {
            Assert.True(pacer.TryAccept(100 + i, 1000 + i));
        }

        Assert.False(pacer.TryAccept(104, 1004));
    }

    [Fact]
    public void TryAccept_FifthJumpNeedsMeanOfLastThreeIntervalsAtLeast643Milliseconds()
    {
        WorldJumpPacer pacer = new();
        long start = 1000;
        long second = start + 10;
        Assert.True(pacer.TryAccept(1, start));
        Assert.True(pacer.TryAccept(2, second));
        Assert.True(pacer.TryAccept(3, second + 20));
        Assert.True(pacer.TryAccept(4, second + 30));

        // The window starts at the second sample once the first leaves the ring.
        Assert.False(pacer.TryAccept(5, second + s_window - 1));
        Assert.True(pacer.TryAccept(5, second + s_window));

        // Refusals do not enter the ring: the next window starts at the third sample.
        Assert.False(pacer.TryAccept(6, second + 20 + s_window - 1));
        Assert.True(pacer.TryAccept(6, second + 20 + s_window));
    }

    [Fact]
    public void TryAccept_SteadyNativeMinimumJumpCadencePasses()
    {
        // 20 frames at the 33 ms floor is the shortest native ground jump.
        long native = 660 * Stopwatch.Frequency / 1000;
        WorldJumpPacer pacer = new();
        for (uint i = 0; i < 12; i++)
        {
            Assert.True(pacer.TryAccept(100 + i, 1000 + i * native));
        }
    }

    [Fact]
    public void TryAccept_RejectsDuplicateBackwardsAndRegressedTimestampsWithoutMutation()
    {
        WorldJumpPacer pacer = new();
        Assert.True(pacer.TryAccept(100, 2000));
        Assert.False(pacer.TryAccept(100, 3000));
        Assert.False(pacer.TryAccept(99, 3000));
        Assert.False(pacer.TryAccept(101, 1999));
        Assert.True(pacer.TryAccept(101, 2000));
    }

    [Fact]
    public void TryAccept_HandlesUInt32TimestampWrap()
    {
        WorldJumpPacer pacer = new();
        Assert.True(pacer.TryAccept(uint.MaxValue - 1, 1000));
        Assert.True(pacer.TryAccept(1, 1001));
        Assert.False(pacer.TryAccept(uint.MaxValue - 1, 1002));
    }
}
