using OpenConquer.AccountServer.Login.Handshake;

namespace OpenConquer.AccountServer.Tests.Login.Handshake;

public sealed class LoginHandshakeConfigurationTests
{
    private static readonly TimeSpan s_maximumPhaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    [Fact]
    public void Constructor_PreservesValidConfiguration()
    {
        TimeSpan phaseTimeout = TimeSpan.FromSeconds(30);

        LoginHandshakeConfiguration configuration = new(phaseTimeout);

        Assert.Equal(phaseTimeout, configuration.PhaseTimeout);
    }

    [Fact]
    public void Constructor_AcceptsMaximumSupportedPhaseTimeout()
    {
        LoginHandshakeConfiguration configuration = new(s_maximumPhaseTimeout);

        Assert.Equal(s_maximumPhaseTimeout, configuration.PhaseTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositivePhaseTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginHandshakeConfiguration(TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("phaseTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsPhaseTimeoutAboveRuntimeTimerLimit()
    {
        TimeSpan phaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFF);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginHandshakeConfiguration(phaseTimeout));

        Assert.Equal("phaseTimeout", exception.ParamName);
    }
}
