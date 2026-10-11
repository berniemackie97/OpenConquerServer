namespace OpenConquer.GameServer.Time;

internal sealed class SystemGameTickSource : IGameTickSource
{
    public uint CurrentTick => unchecked((uint)Environment.TickCount);
}
