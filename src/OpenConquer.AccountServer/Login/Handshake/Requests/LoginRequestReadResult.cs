using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake.Requests;

/// <summary>
/// The result of reading the account credential request from a login connection.
/// </summary>
internal sealed class LoginRequestReadResult : IDisposable
{
    private LoginRequestReadResult(LoginRequestReadStatus status, LoginAccountRequest? request, ushort? unexpectedPacketId,
        LoginAccountRegistrationRequest? registration = null)
    {
        Status = status;
        Request = request;
        UnexpectedPacketId = unexpectedPacketId;
        Registration = registration;
    }

    public LoginRequestReadStatus Status { get; }

    /// <summary>
    /// The decoded request when <see cref="Status"/> is <see cref="LoginRequestReadStatus.AuthenticationRequest"/>.
    /// </summary>
    /// <remarks>
    /// The result owns this request and wipes its password on disposal.
    /// </remarks>
    public LoginAccountRequest? Request { get; }

    /// <summary>
    /// Gets the received packet identifier when <see cref="Status"/> is
    /// <see cref="LoginRequestReadStatus.UnexpectedPacket"/>.
    /// </summary>
    public ushort? UnexpectedPacketId { get; }

    /// <summary>
    /// The decoded registration when <see cref="Status"/> is <see cref="LoginRequestReadStatus.RegistrationRequest"/>.
    /// The result owns this request and wipes its password on disposal.
    /// </summary>
    public LoginAccountRegistrationRequest? Registration { get; }

    public static LoginRequestReadResult AuthenticationRequest(LoginAccountRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new LoginRequestReadResult(LoginRequestReadStatus.AuthenticationRequest, request, unexpectedPacketId: null);
    }

    public static LoginRequestReadResult EndOfStream()
    {
        return new LoginRequestReadResult(LoginRequestReadStatus.EndOfStream, request: null, unexpectedPacketId: null);
    }

    public static LoginRequestReadResult UnexpectedPacket(ushort packetId)
    {
        return new LoginRequestReadResult(LoginRequestReadStatus.UnexpectedPacket, request: null, packetId);
    }

    public static LoginRequestReadResult RegistrationRequest(LoginAccountRegistrationRequest registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new LoginRequestReadResult(LoginRequestReadStatus.RegistrationRequest, request: null, unexpectedPacketId: null, registration);
    }

    public static LoginRequestReadResult InvalidRegistrationRequest()
    {
        return new LoginRequestReadResult(LoginRequestReadStatus.InvalidRegistrationRequest, request: null, unexpectedPacketId: null);
    }

    public static LoginRequestReadResult InvalidAccountRequest()
    {
        return new LoginRequestReadResult(LoginRequestReadStatus.InvalidAccountRequest, request: null, unexpectedPacketId: null);
    }

    public void Dispose()
    {
        Request?.Dispose();
        Registration?.Dispose();
    }
}
