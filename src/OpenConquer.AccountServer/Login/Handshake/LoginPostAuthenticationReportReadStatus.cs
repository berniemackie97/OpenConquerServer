namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Describes the protocol-level outcome of consuming the native
/// post-authentication AccountServer report sequence.
/// </summary>
internal enum LoginPostAuthenticationReportReadStatus
{
    Success,
    EndOfStream,
    UnexpectedPacket,
    InvalidReport,
    SessionMismatch,
    InvalidMacAddress,
    UnexpectedResourceName,
}
