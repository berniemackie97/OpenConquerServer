using System.Net;
using OpenConquer.AccountServer.Login.Handshake;

namespace OpenConquer.AccountServer.Tests.Login.Handshake;

public sealed class LoginHandshakeConfigurationTests
{
    private static readonly TimeSpan s_maximumPhaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    [Fact]
    public void Constructor_PreservesValidConfiguration()
    {
        TimeSpan phaseTimeout = TimeSpan.FromSeconds(30);

        LoginHandshakeConfiguration configuration = new(IPAddress.Parse("192.168.1.10"), 5816, phaseTimeout);

        Assert.Equal("192.168.1.10", configuration.GameServerIp);
        Assert.Equal(5816u, configuration.GameServerPort);
        Assert.Equal(phaseTimeout, configuration.PhaseTimeout);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Constructor_AcceptsValidTcpPortBoundaries(int port)
    {
        LoginHandshakeConfiguration configuration = new(IPAddress.Loopback, port, TimeSpan.FromSeconds(1));

        Assert.Equal((uint)port, configuration.GameServerPort);
    }

    [Fact]
    public void Constructor_AcceptsMaximumSupportedPhaseTimeout()
    {
        LoginHandshakeConfiguration configuration = new(IPAddress.Loopback, 5816, s_maximumPhaseTimeout);

        Assert.Equal(s_maximumPhaseTimeout, configuration.PhaseTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Constructor_RejectsInvalidTcpPort(int port)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginHandshakeConfiguration(IPAddress.Loopback, port, TimeSpan.FromSeconds(1)));

        Assert.Equal("gameServerPort", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullGameServerAddress()
    {
        Assert.Throws<ArgumentNullException>(() => new LoginHandshakeConfiguration(null!, 5816, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Constructor_RejectsNonIpv4GameServerAddress()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new LoginHandshakeConfiguration(IPAddress.IPv6Loopback, 5816, TimeSpan.FromSeconds(1)));

        Assert.Equal("gameServerAddress", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositivePhaseTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginHandshakeConfiguration(IPAddress.Loopback, 5816, TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("phaseTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsPhaseTimeoutAboveRuntimeTimerLimit()
    {
        TimeSpan phaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFF);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LoginHandshakeConfiguration(IPAddress.Loopback, 5816, phaseTimeout));

        Assert.Equal("phaseTimeout", exception.ParamName);
    }
}
