using System.Text.Json;

namespace OpenConquer.World.LoadTool.Tests;

public sealed class WorldLoadTests
{
    [Fact]
    public void Percentiles_UseNearestRankIncludingSingleSample()
    {
        long[] ticks = Enumerable.Range(1, 100).Reverse().Select(static value => (long)value).ToArray();
        Assert.Equal(new LatencyPercentiles(50, 99, 100), LatencyPercentiles.FromTicks(ticks, 1_000_000));
        Assert.Equal(new LatencyPercentiles(12, 12, 12), LatencyPercentiles.FromTicks([24], 2_000_000));
    }

    [Fact]
    public void Percentiles_RejectEmptyOrNegativeSamplesAndInvalidFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyPercentiles.FromTicks([], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyPercentiles.FromTicks([-1], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyPercentiles.FromTicks([1], 0));
    }

    [Theory]
    [InlineData("--unknown", "1")]
    [InlineData("--sessions", "-1")]
    [InlineData("--sessions", "8193")]
    [InlineData("--maps", "0")]
    [InlineData("--steps", "1000000")]
    [InlineData("--timeout-seconds", "0")]
    public void Options_RejectUnknownMalformedAndUnboundedLoads(string name, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => WorldLoadOptions.Parse([name, value]));
    }

    [Fact]
    public void Options_RejectIncompleteAndDuplicateArguments()
    {
        Assert.Throws<ArgumentException>(() => WorldLoadOptions.Parse(["--sessions"]));
        Assert.Throws<ArgumentException>(() => WorldLoadOptions.Parse(["--sessions", "2", "--sessions", "4"]));
    }

    [Fact]
    public async Task Run_UsesActualRuntimeAndMatchesGoldenWalkLoops()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "walk-loops.json");
        GoldenWalkLoop[] cases = Assert.IsType<GoldenWalkLoop[]>(
            JsonSerializer.Deserialize<GoldenWalkLoop[]>(await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken)));
        Assert.NotEmpty(cases);
        foreach (GoldenWalkLoop expected in cases)
        {
            WorldLoadReport report = await new WorldLoadRunner(TimeProvider.System).RunAsync(expected.Options, TestContext.Current.CancellationToken);
            Assert.Equal(expected.CompletedCommands, report.Measurement.CompletedCommands);
            Assert.Equal(expected.VerifiedCharacters, report.VerifiedCharacters);
            Assert.Equal(expected.FinalY, report.FinalY);
            Assert.True(report.Measurement.Completion.P99Microseconds >= report.Measurement.Completion.P50Microseconds);
            Assert.True(report.Measurement.ProcessAllocatedBytes >= report.Measurement.SubmissionAllocatedBytes);
            Assert.True(report.Measurement.SubmissionBytesPerCommand > 0);
            Assert.True(report.Measurement.CommandsPerSecond > 0);
            string json = JsonSerializer.Serialize(report, WorldLoadJsonContext.Default.WorldLoadReport);
            Assert.Equal(report, JsonSerializer.Deserialize(json, WorldLoadJsonContext.Default.WorldLoadReport));
        }
    }

    [Fact]
    public async Task Run_PreCanceledOperationDoesNotProduceASuccessReport()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorldLoadRunner(TimeProvider.System).RunAsync(new WorldLoadOptions(), canceled.Token));
    }

    private sealed record GoldenWalkLoop(WorldLoadOptions Options, int CompletedCommands, int VerifiedCharacters, ushort FinalY);
}
