using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Publishes the validated map content together with the running world runtime.
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

    public MapTransitionCatalog GetRequiredTransitions()
    {
        lock (_gate)
        {
            if (_publication is not { } publication || !publication.Runtime.IsRunning)
            {
                throw new InvalidOperationException("The authoritative GameServer world is not ready.");
            }

            return publication.Transitions;
        }
    }

    internal void Publish(WorldMapContentCatalog maps, MapTransitionCatalog transitions, WorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(runtime);

        if (!ReferenceEquals(maps, transitions.Maps) || !runtime.UsesMapContent(maps))
        {
            throw new InvalidDataException("The transition catalog, map content and world runtime do not share one authoritative snapshot.");
        }

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

            _publication = new WorldPublication(maps, transitions, runtime);
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

    private sealed record WorldPublication(WorldMapContentCatalog Maps, MapTransitionCatalog Transitions, WorldRuntime Runtime);
}
