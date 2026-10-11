namespace OpenConquer.GameServer.World.Execution;

internal interface IWorldPartitionCommand
{
    void Execute(MapInstance map);
    void Reject(Exception exception);
}
