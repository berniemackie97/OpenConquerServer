using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Realms;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Admission;

/// <summary>
/// Refuses logins while the realm is full or busy. TQ's world server answered such a login with a talk on
/// the login channel and then closed the connection; the 5517 client shows any login-channel text other
/// than its control words as a login error.
/// </summary>
internal sealed class GameLoginCapacityGate(IRealmLoadSource loadSource)
{
    private const ushort LoginChannel = 0x835;

    private static readonly GameChatPacket s_full = CreateRefusal("The server is full. Please try again later.");
    private static readonly GameChatPacket s_busy = CreateRefusal("The server is busy. Please try again later.");

    private readonly IRealmLoadSource _loadSource = loadSource ?? throw new ArgumentNullException(nameof(loadSource));

    /// <summary>
    /// Returns the talk to send before closing the connection, or <see langword="null"/> when the login may continue.
    /// </summary>
    public GameChatPacket? GetRefusal() => _loadSource.Sample().Load switch
    {
        RealmLoad.Full => s_full,
        RealmLoad.Busy => s_busy,
        _ => null,
    };

    private static GameChatPacket CreateRefusal(string message) =>
        new(color: 0, channel: LoginChannel, style: 0, identity: 0, sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: message);
}
