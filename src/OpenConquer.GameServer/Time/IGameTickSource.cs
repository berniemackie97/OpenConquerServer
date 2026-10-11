namespace OpenConquer.GameServer.Time;

/// <summary>
/// Supplies the 32-bit millisecond tick used by GameServer protocol timestamps.
/// </summary>
internal interface IGameTickSource
{
    uint CurrentTick { get; }
}
