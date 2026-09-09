using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameLoginTicketExpirationCleanerPrecisionTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 2)]
    public void GetExpirationGraceMicroseconds_RoundsTowardLongerGrace(
        long ticks,
        long expectedMicroseconds
    )
    {
        long actual = GameLoginTicketExpirationCleaner.GetExpirationGraceMicroseconds(
            TimeSpan.FromTicks(ticks)
        );

        Assert.Equal(expectedMicroseconds, actual);
    }

    [Fact]
    public void GetExpirationGraceMicroseconds_ExactProductionGraceIsPreserved()
    {
        TimeSpan expirationGrace = TimeSpan.FromMinutes(5);

        long microseconds = GameLoginTicketExpirationCleaner.GetExpirationGraceMicroseconds(
            expirationGrace
        );

        Assert.Equal(expirationGrace.Ticks / TimeSpan.TicksPerMicrosecond, microseconds);
    }

    [Fact]
    public void GetExpirationGraceMicroseconds_SubMicrosecondRemainderRoundsUp()
    {
        TimeSpan exactGrace = TimeSpan.FromMinutes(5);
        TimeSpan configuredGrace = exactGrace.Add(TimeSpan.FromTicks(1));

        long microseconds = GameLoginTicketExpirationCleaner.GetExpirationGraceMicroseconds(
            configuredGrace
        );

        Assert.Equal((exactGrace.Ticks / TimeSpan.TicksPerMicrosecond) + 1, microseconds);
    }
}
