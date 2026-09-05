using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Represents the result of consuming the account credential request from a login connection.
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
    public LoginAccountRequest? Request { get; }
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
