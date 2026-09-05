namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Identifies the native post authentication AccountServer report currently expected from the client.
/// </summary>
internal enum LoginPostAuthenticationReportPhase
{
    MacAddressReport,
    ResourceVersionReport,
}
