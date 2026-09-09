using System.Net;
using System.Net.Sockets;

namespace OpenConquer.AccountServer.Login.Handshake;

internal sealed class LoginHandshakeConfiguration
{
    private static readonly TimeSpan s_maximumPhaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    public LoginHandshakeConfiguration(IPAddress gameServerAddress, int gameServerPort, TimeSpan phaseTimeout)
    {
        ArgumentNullException.ThrowIfNull(gameServerAddress);

        if (gameServerAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("The game-server login endpoint must use IPv4.", nameof(gameServerAddress));
        }

        if (gameServerPort is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(gameServerPort), "The game-server port must be between 1 and 65535.");
        }

        if (phaseTimeout <= TimeSpan.Zero || phaseTimeout > s_maximumPhaseTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(phaseTimeout), $"The login-handshake phase timeout must be greater than zero and no greater than {s_maximumPhaseTimeout}.");
        }

        GameServerIp = gameServerAddress.ToString();
        GameServerPort = checked((uint)gameServerPort);
        PhaseTimeout = phaseTimeout;
    }

    public string GameServerIp { get; }
    public uint GameServerPort { get; }
    public TimeSpan PhaseTimeout { get; }
}
