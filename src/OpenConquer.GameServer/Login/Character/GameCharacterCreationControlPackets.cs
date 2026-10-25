using OpenConquer.Application.Characters.Creation;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Character;

/// <summary>
/// The 5517 create-role control messages. Sending them creates nothing; only send success after the character
/// has been saved.
/// </summary>
internal static class GameCharacterCreationControlPackets
{
    private const ushort LoginChannel = 0x835;
    private const ushort CreationChannel = 0x834;

    private static readonly GameChatPacket s_newRole = new(color: 0, channel: LoginChannel, style: 0, identity: 0,
        sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: "NEW_ROLE");

    private static readonly GameChatPacket s_accepted = new(color: 0, channel: CreationChannel, style: 0, identity: 0,
        sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: "ANSWER_OK");

    private static readonly GameChatPacket s_invalidName = CreateRejection("Invalid character name.");
    private static readonly GameChatPacket s_invalidSelection = CreateRejection("Invalid character selection.");
    private static readonly GameChatPacket s_nameTaken = CreateRejection("Character name is already in use.");

    public static GameChatPacket NewRole => s_newRole;
    public static GameChatPacket Accepted => s_accepted;

    // These are server-defined messages. 5517 forwards non-success
    // creation-channel text to its role-creation error UI.
    public static GameChatPacket Rejected(CharacterCreationStatus status) => status switch
    {
        CharacterCreationStatus.InvalidName => s_invalidName,
        CharacterCreationStatus.InvalidSelection => s_invalidSelection,
        CharacterCreationStatus.NameAlreadyInUse => s_nameTaken,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported creation refusal."),
    };

    private static GameChatPacket CreateRejection(string message) =>
        new(color: 0, channel: CreationChannel, style: 0, identity: 0,
            sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: message);
}
