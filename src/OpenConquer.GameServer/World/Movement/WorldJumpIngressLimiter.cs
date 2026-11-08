using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>Fixed-memory, per-session budget for otherwise valid jump intents.</summary>
/// <remarks>Called only by the serialized game-session reader; independent of movement pacing.</remarks>
internal sealed class WorldJumpIngressLimiter
{
    internal const int MaximumAttemptsPerSecond = 8;
    private static readonly long s_windowTicks = Stopwatch.Frequency;
    private long _windowStart;
    private long _lastAcceptedTimestamp;
    private int _count;

    public bool TryAccept(long timestamp)
    {
        if (_count != 0 && timestamp < _lastAcceptedTimestamp)
        {
            return false;
        }

        if (_count == 0 || timestamp - _windowStart >= s_windowTicks)
        {
            _windowStart = timestamp;
            _count = 0;
        }

        if (_count >= MaximumAttemptsPerSecond)
        {
            return false;
        }

        _count++;
        _lastAcceptedTimestamp = timestamp;
        return true;
    }
}
