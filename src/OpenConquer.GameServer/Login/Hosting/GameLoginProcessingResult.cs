using OpenConquer.GameServer.World.Sessions;

namespace OpenConquer.GameServer.Login.Hosting;

// Exactly one successful handoff owns each admitted transport.
internal readonly record struct GameLoginProcessingResult
{
    public GameLoginProcessingResult(WorldSession? worldSession, Task<WorldSession?>? pendingCreation)
    {
        if (worldSession is not null && pendingCreation is not null)
        {
            throw new ArgumentException("A game connection cannot enter the world and character creation simultaneously.");
        }

        WorldSession = worldSession;
        PendingCreation = pendingCreation;
    }

    public WorldSession? WorldSession { get; }
    public Task<WorldSession?>? PendingCreation { get; }
}
