namespace OpenConquer.AccountServer.Login.Handshake.PostAuthentication;

internal enum LoginPostAuthenticationReportReadStatus
{
    Success,
    EndOfStream,
    TimedOut,
    UnexpectedPacket,
    InvalidReport,
    SessionMismatch,
    InvalidMacAddress,
    UnexpectedResourceName,
}
