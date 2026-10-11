using OpenConquer.Application.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Publishes one validated canonical content snapshot together with its running authoritative world.
/// </summary>
internal sealed class GameServerWorldState
{
    private readonly Lock _gate = new();
    private WorldPublication? _publication;

    public (WorldMapContentCatalog Maps, WorldRuntime Runtime) GetRequiredWorld()
    {
        lock (_gate)
        {
            if (_publication is not { } publication || !publication.Runtime.IsRunning)
            {
                throw new InvalidOperationException("The authoritative GameServer world is not ready.");
            }

            return (publication.Maps, publication.Runtime);
        }
    }

    internal void Publish(WorldMapContentCatalog maps, WorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(runtime);

        lock (_gate)
        {
            if (_publication is not null)
            {
                throw new InvalidOperationException("The authoritative GameServer world has already been published.");
            }

            if (!runtime.IsRunning)
            {
                throw new InvalidOperationException("An inactive world runtime cannot be published.");
            }

            _publication = new WorldPublication(maps, runtime);
        }
    }

    internal void Unpublish(WorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        lock (_gate)
        {
            if (ReferenceEquals(_publication?.Runtime, runtime))
            {
                _publication = null;
            }
        }
    }

    private sealed record WorldPublication(WorldMapContentCatalog Maps, WorldRuntime Runtime);
}
