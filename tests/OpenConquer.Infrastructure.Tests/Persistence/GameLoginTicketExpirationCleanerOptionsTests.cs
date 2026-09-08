using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameLoginTicketExpirationCleanerOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        GameLoginTicketExpirationCleanerOptions options = new();

        Assert.Equal(TimeSpan.FromMinutes(5), options.ExpirationGrace);
        Assert.Equal(1_000, options.MaximumBatchSize);
    }

    [Fact]
    public void Constructor_CustomValuesArePreserved()
    {
        GameLoginTicketExpirationCleanerOptions options = new(
            expirationGrace: TimeSpan.FromMinutes(10),
            maximumBatchSize: 500
        );

        Assert.Equal(TimeSpan.FromMinutes(10), options.ExpirationGrace);
        Assert.Equal(500, options.MaximumBatchSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonpositiveExpirationGraceThrows(int ticks)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketExpirationCleanerOptions(expirationGrace: TimeSpan.FromTicks(ticks))
        );

        Assert.Equal("expirationGrace", exception.ParamName);
    }

    [Fact]
    public void Constructor_ExpirationGraceGreaterThanOneDayThrows()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketExpirationCleanerOptions(
                expirationGrace: TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)
            )
        );

        Assert.Equal("expirationGrace", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Constructor_InvalidMaximumBatchSizeThrows(int maximumBatchSize)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketExpirationCleanerOptions(maximumBatchSize: maximumBatchSize)
        );

        Assert.Equal("maximumBatchSize", exception.ParamName);
    }

    [Fact]
    public void Constructor_BoundaryValuesAreAccepted()
    {
        GameLoginTicketExpirationCleanerOptions options = new(
            expirationGrace: TimeSpan.FromDays(1),
            maximumBatchSize: 10_000
        );

        Assert.Equal(TimeSpan.FromDays(1), options.ExpirationGrace);
        Assert.Equal(10_000, options.MaximumBatchSize);
    }
}
