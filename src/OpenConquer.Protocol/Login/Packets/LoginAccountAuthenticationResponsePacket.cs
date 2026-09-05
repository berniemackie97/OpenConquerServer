using System.Net;
using System.Net.Sockets;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Represents the server -> client account authentication response packet 1055.
/// </summary>
public sealed class LoginAccountAuthenticationResponsePacket : IPacket
{
    public const ushort PacketIdentifier = 1055;

    public const int GameServerIpFieldLength = 16;
    public const int MaximumGameServerIpTextLength = GameServerIpFieldLength - 1;
    public const int PayloadSize = (sizeof(uint) * 4) + GameServerIpFieldLength;

    private LoginAccountAuthenticationResponsePacket(uint sessionUid, uint authenticationKeyOrFailureCode, uint gameServerPort, uint additionalSessionField, string gameServerIp)
    {
        SessionUid = sessionUid;
        AuthenticationKeyOrFailureCode = authenticationKeyOrFailureCode;
        GameServerPort = gameServerPort;
        AdditionalSessionField = additionalSessionField;
        GameServerIp = gameServerIp;
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public bool IsSuccess => SessionUid != 0;
    public uint SessionUid { get; }
    public uint AuthenticationKeyOrFailureCode { get; }
    public uint GameServerPort { get; }
    public uint AdditionalSessionField { get; }
    public string GameServerIp { get; }

    public static LoginAccountAuthenticationResponsePacket Success(uint sessionUid, uint authenticationKey, uint gameServerPort, uint additionalSessionField, string gameServerIp)
    {
        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A successful account-authentication response requires a nonzero session UID.");
        }

        return new LoginAccountAuthenticationResponsePacket(sessionUid, authenticationKey, gameServerPort, additionalSessionField, NormalizeGameServerIp(gameServerIp));
    }

    public static LoginAccountAuthenticationResponsePacket Failure(LoginAccountAuthenticationFailureCode failureCode, uint gameServerPort, string gameServerIp)
    {
        if (!Enum.IsDefined(failureCode))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode), failureCode, "Unknown account-authentication failure code.");
        }

        return new LoginAccountAuthenticationResponsePacket(sessionUid: 0, authenticationKeyOrFailureCode: (uint)failureCode, gameServerPort, additionalSessionField: 0, NormalizeGameServerIp(gameServerIp));
    }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(SessionUid);
        writer.WriteUInt32(AuthenticationKeyOrFailureCode);
        writer.WriteUInt32(GameServerPort);
        writer.WriteUInt32(AdditionalSessionField);
        writer.WriteFixedString(GameServerIp, GameServerIpFieldLength, TqTextEncoding.Ascii);
    }

    private static string NormalizeGameServerIp(string gameServerIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameServerIp);

        if (!IPAddress.TryParse(gameServerIp, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Game-server IP must be a valid IPv4 address.", nameof(gameServerIp));
        }

        string normalized = address.ToString();

        if (normalized.Length > MaximumGameServerIpTextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(gameServerIp), $"Game-server IPv4 address must fit within {MaximumGameServerIpTextLength} ASCII bytes.");
        }

        return normalized;
    }
}
