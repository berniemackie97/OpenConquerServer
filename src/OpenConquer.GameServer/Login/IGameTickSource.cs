namespace OpenConquer.GameServer.Login;

/// <summary>
/// Supplies the 32-bit millisecond tick used by native GameServer protocol timestamps.
/// </summary>
internal interface IGameTickSource
{
    uint CurrentTick { get; }
}

internal sealed class SystemGameTickSource : IGameTickSource
{
    public uint CurrentTick => unchecked((uint)Environment.TickCount);
}
