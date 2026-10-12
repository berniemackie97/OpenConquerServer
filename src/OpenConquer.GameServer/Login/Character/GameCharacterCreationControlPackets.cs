using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Character;

/// <summary>
/// Native 5517 create-role control talks. These packets do not authorize character creation.
/// The caller is responsible for sending success only after confirmed persistence.
/// </summary>
internal static class GameCharacterCreationControlPackets
{
    private const ushort LoginChannel = 0x835;
    private const ushort CreationChannel = 0x834;

    private static readonly GameChatPacket s_newRole = new(color: 0, channel: LoginChannel, style: 0, identity: 0,
        sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: "NEW_ROLE");

    private static readonly GameChatPacket s_accepted = new(color: 0, channel: CreationChannel, style: 0, identity: 0,
        sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: "ANSWER_OK");

    public static GameChatPacket NewRole => s_newRole;
    public static GameChatPacket Accepted => s_accepted;
}
