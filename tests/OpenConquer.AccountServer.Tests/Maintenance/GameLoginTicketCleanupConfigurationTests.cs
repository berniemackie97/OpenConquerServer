using OpenConquer.AccountServer.Maintenance;

namespace OpenConquer.AccountServer.Tests.Maintenance;

public sealed class GameLoginTicketCleanupConfigurationTests
{
    [Fact]
    public void Constructor_AcceptsValidConfiguration()
    {
        GameLoginTicketCleanupConfiguration configuration = new(TimeSpan.FromMinutes(1), maximumBatchesPerRun: 10);

        Assert.Equal(TimeSpan.FromMinutes(1), configuration.Interval);
        Assert.Equal(10, configuration.MaximumBatchesPerRun);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Constructor_RejectsInvalidMaximumBatchesPerRun(int maximumBatchesPerRun)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLoginTicketCleanupConfiguration(TimeSpan.FromMinutes(1), maximumBatchesPerRun));
    }

    [Fact]
    public void Constructor_RejectsTooShortInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLoginTicketCleanupConfiguration(TimeSpan.FromMilliseconds(999), maximumBatchesPerRun: 1));
    }

    [Fact]
    public void Constructor_RejectsTooLongInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLoginTicketCleanupConfiguration(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1), maximumBatchesPerRun: 1));
    }
}
