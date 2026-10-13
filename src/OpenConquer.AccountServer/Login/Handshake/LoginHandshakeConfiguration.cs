namespace OpenConquer.AccountServer.Login.Handshake;

internal sealed class LoginHandshakeConfiguration
{
    private static readonly TimeSpan s_maximumPhaseTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    public LoginHandshakeConfiguration(TimeSpan phaseTimeout)
    {
        if (phaseTimeout <= TimeSpan.Zero || phaseTimeout > s_maximumPhaseTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(phaseTimeout), $"The login-handshake phase timeout must be greater than zero and no greater than {s_maximumPhaseTimeout}.");
        }

        PhaseTimeout = phaseTimeout;
    }

    public TimeSpan PhaseTimeout { get; }
}
