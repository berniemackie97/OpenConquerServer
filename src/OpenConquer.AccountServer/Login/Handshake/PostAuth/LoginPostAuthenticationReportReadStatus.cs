namespace OpenConquer.AccountServer.Login.Handshake.PostAuth;

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
