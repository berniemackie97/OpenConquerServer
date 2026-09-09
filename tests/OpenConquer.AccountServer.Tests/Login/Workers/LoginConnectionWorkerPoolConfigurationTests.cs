using OpenConquer.AccountServer.Login.Workers;

namespace OpenConquer.AccountServer.Tests.Login.Workers;

public sealed class LoginConnectionWorkerPoolConfigurationTests
{
    private static readonly TimeSpan s_maximumConnectionTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    [Fact]
    public void Constructor_PreservesValidConfiguration()
    {
        TimeSpan connectionTimeout = TimeSpan.FromSeconds(30);

        LoginConnectionWorkerPoolConfiguration configuration = new(4, connectionTimeout);

        Assert.Equal(4, configuration.WorkerCount);
        Assert.Equal(connectionTimeout, configuration.ConnectionTimeout);
    }

    [Fact]
    public void Constructor_AcceptsSingleWorker()
    {
        LoginConnectionWorkerPoolConfiguration configuration = new(1, TimeSpan.FromSeconds(1));

        Assert.Equal(1, configuration.WorkerCount);
    }

    [Fact]
    public void Constructor_AcceptsMaximumSupportedConnectionTimeout()
    {
        LoginConnectionWorkerPoolConfiguration configuration = new(1, s_maximumConnectionTimeout);

        Assert.Equal(s_maximumConnectionTimeout, configuration.ConnectionTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveWorkerCount(int workerCount)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginConnectionWorkerPoolConfiguration(workerCount, TimeSpan.FromSeconds(1)));

        Assert.Equal("workerCount", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveConnectionTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("connectionTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsConnectionTimeoutAboveRuntimeTimerLimit()
    {
        TimeSpan connectionTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFF);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginConnectionWorkerPoolConfiguration(1, connectionTimeout));

        Assert.Equal("connectionTimeout", exception.ParamName);
    }
}
