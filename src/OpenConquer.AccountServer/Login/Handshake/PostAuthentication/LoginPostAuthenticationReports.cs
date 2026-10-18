namespace OpenConquer.AccountServer.Login.Handshake.PostAuthentication;

/// <summary>
/// Validated telemetry the client reports after a successful login response.
/// </summary>
/// <remarks>
/// The client controls all of it. Never treat it, and the MAC address in particular, as proof of identity.
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
