namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Identifies the post-authentication account login report currently expected from the client.
/// </summary>
internal enum LoginPostAuthenticationReportPhase
{
    MacAddressReport,
    ResourceVersionReport,
}
