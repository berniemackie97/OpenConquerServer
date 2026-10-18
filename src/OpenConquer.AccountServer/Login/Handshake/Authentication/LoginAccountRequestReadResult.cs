using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake.Authentication;

/// <summary>
/// The result of reading the account credential request from a login connection.
/// </summary>
internal sealed class LoginAccountRequestReadResult
{
    private LoginAccountRequestReadResult(LoginAccountRequestReadStatus status, LoginAccountRequest? request, ushort? unexpectedPacketId)
    {
        Status = status;
        Request = request;
        UnexpectedPacketId = unexpectedPacketId;
    }

    public LoginAccountRequestReadStatus Status { get; }

    /// <summary>
    /// The decoded request when <see cref="Status"/> is <see cref="LoginAccountRequestReadStatus.Success"/>.
    /// </summary>
    /// <remarks>
    /// The caller must dispose it; it holds the password in mutable storage.
    /// </remarks>
    public LoginAccountRequest? Request { get; }

    /// <summary>
    /// Gets the received packet identifier when <see cref="Status"/> is
    /// <see cref="LoginAccountRequestReadStatus.UnexpectedPacket"/>.
    /// </summary>
    public ushort? UnexpectedPacketId { get; }

    public static LoginAccountRequestReadResult Success(LoginAccountRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new LoginAccountRequestReadResult(LoginAccountRequestReadStatus.Success, request, unexpectedPacketId: null);
    }

    public static LoginAccountRequestReadResult EndOfStream()
    {
        return new LoginAccountRequestReadResult(LoginAccountRequestReadStatus.EndOfStream, request: null, unexpectedPacketId: null);
    }

    public static LoginAccountRequestReadResult UnexpectedPacket(ushort packetId)
    {
        return new LoginAccountRequestReadResult(LoginAccountRequestReadStatus.UnexpectedPacket, request: null, packetId);
    }

    public static LoginAccountRequestReadResult InvalidAccountRequest()
    {
        return new LoginAccountRequestReadResult(LoginAccountRequestReadStatus.InvalidAccountRequest, request: null, unexpectedPacketId: null);
    }
}
