namespace OpenConquer.AccountServer.Login.Handshake.Requests;

/// <summary>
/// Describes the protocol-level outcome of reading the account credential
/// request from an established login connection.
/// </summary>
internal enum LoginRequestReadStatus
{
    AuthenticationRequest,
    EndOfStream,
    UnexpectedPacket,
    InvalidAccountRequest,
    RegistrationRequest,
    InvalidRegistrationRequest,
}
