namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Describes the protocol-level outcome of reading the account credential
/// request from an established login connection.
/// </summary>
internal enum LoginAccountRequestReadStatus
{
    Success,
    EndOfStream,
    UnexpectedPacket,
    InvalidAccountRequest,
}
