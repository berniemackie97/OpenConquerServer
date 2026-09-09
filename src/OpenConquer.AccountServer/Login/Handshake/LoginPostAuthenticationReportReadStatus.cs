namespace OpenConquer.AccountServer.Login.Handshake;

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
