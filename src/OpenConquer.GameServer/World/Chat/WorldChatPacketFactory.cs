using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.World.Chat;

/// <summary>Constructs only server-authorized, client-renderable 5517 chat.</summary>
internal static class WorldChatPacketFactory
{
    public static GameChatPacket Create(uint senderId, string senderName, string recipientName, GameInboundChat request)
    {
        ArgumentNullException.ThrowIfNull(senderName);
        ArgumentNullException.ThrowIfNull(recipientName);
        if (!request.IsSupported || senderId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Chat must have a supported channel and authenticated sender.");
        }

        string message = request.IsRoleWord && request.Message.Length > GameInboundChat.MaximumRoleWordBytes
            ? request.Message[..GameInboundChat.MaximumRoleWordBytes]
            : request.Message;

        return new GameChatPacket(uint.MaxValue, request.Channel, 0, senderId,
            senderName, recipientName, string.Empty, message);
    }
}
