using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Realms;

/// <summary>
/// Derives realm load the way TQ's world server did before reporting to its AccountServer: full at the
/// character limit, otherwise busy while map execution has stopped keeping up with its work.
/// </summary>
/// <remarks>
/// TQ marked a world busy when a map group's message backlog passed a fixed depth. Map partition queues
/// here are bounded, so the equivalent signal is a partition whose queue is full and refusing commands.
/// </remarks>
internal sealed class WorldRealmLoadSource(WorldRuntime runtime) : IRealmLoadSource
{
    private readonly WorldRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public RealmLoadSample Sample()
    {
        int onlineCount = _runtime.ResidentCount;

        RealmLoad load = onlineCount >= _runtime.MaxCharacters ? RealmLoad.Full
            : _runtime.IsCongested ? RealmLoad.Busy
            : RealmLoad.Normal;

        return new RealmLoadSample(load, onlineCount);
    }
}
