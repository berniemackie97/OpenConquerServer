using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Realms;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Admission;

/// <summary>
/// Refuses logins while the realm is full or busy.
/// </summary>
internal sealed class GameLoginCapacityGate(IRealmLoadSource loadSource)
{
    private readonly IRealmLoadSource _loadSource = loadSource ?? throw new ArgumentNullException(nameof(loadSource));

    /// <summary>
    /// Returns the talk to send before closing the connection, or <see langword="null"/> when the login may continue.
    /// </summary>
    public GameChatPacket? GetRefusal() => _loadSource.Sample().Load switch
    {
        RealmLoad.Full => GameLoginRefusals.RealmFull,
        RealmLoad.Busy => GameLoginRefusals.RealmBusy,
        _ => null,
    };
}
