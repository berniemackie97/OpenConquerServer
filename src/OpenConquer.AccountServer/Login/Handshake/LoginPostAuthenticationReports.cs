namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Contains the validated client controlled telemetry reported after a successful AccountServer authentication response.
/// </summary>
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
