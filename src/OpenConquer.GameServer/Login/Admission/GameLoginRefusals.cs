using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Admission;

/// <summary>
/// Talks that refuse a login on the login channel before the connection is closed, as TQ's world server did.
/// The 5517 client shows any login-channel text other than its control words as a login error.
/// </summary>
internal static class GameLoginRefusals
{
    private const ushort LoginChannel = 0x835;

    public static GameChatPacket RealmFull { get; } = Create("The server is full. Please try again later.");
    public static GameChatPacket RealmBusy { get; } = Create("The server is busy. Please try again later.");
    public static GameChatPacket AlreadyOnline { get; } = Create("Your account is still online. Please try again later.");

    private static GameChatPacket Create(string message) =>
        new(color: 0, channel: LoginChannel, style: 0, identity: 0, sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: message);
}
