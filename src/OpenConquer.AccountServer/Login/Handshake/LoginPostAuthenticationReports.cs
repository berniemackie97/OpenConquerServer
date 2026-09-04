namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Contains the validated client-controlled telemetry reported after a
/// successful AccountServer authentication response.
/// </summary>
/// <remarks>
/// These values are not authentication proof. In particular, the MAC address is
/// entirely client controlled and must not be treated as a trusted hardware
/// identity.
/// </remarks>
internal sealed class LoginPostAuthenticationReports
{
    public LoginPostAuthenticationReports(string macAddress, int resourceVersion)
    {
        ArgumentNullException.ThrowIfNull(macAddress);

        MacAddress = macAddress;
        ResourceVersion = resourceVersion;
    }

    public string MacAddress { get; }
    public int ResourceVersion { get; }
}
