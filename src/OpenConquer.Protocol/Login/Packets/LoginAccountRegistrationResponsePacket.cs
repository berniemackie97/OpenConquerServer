using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Server-to-client account-registration response (packet 0xF002), an OpenConquer extension.
/// </summary>
/// <remarks>
/// <code>
/// +0x00 UInt32 result code
/// </code>
/// The server closes the connection after sending it. A registration never issues a game-login ticket.
/// </remarks>
public sealed class LoginAccountRegistrationResponsePacket : IPacket
{
    public const ushort PacketIdentifier = 0xF002;
    public const int PayloadSize = sizeof(uint);

    public LoginAccountRegistrationResponsePacket(LoginAccountRegistrationResultCode resultCode)
    {
        if (!Enum.IsDefined(resultCode))
        {
            throw new ArgumentOutOfRangeException(nameof(resultCode), resultCode, "Unknown account-registration result code.");
        }

        ResultCode = resultCode;
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public LoginAccountRegistrationResultCode ResultCode { get; }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32((uint)ResultCode);
    }
}
