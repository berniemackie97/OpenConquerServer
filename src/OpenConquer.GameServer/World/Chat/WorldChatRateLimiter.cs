using System.Diagnostics;

namespace OpenConquer.GameServer.World.Chat;

/// <summary>
/// Bounded per-session chat fanout. The serialized reader waits when saturated,
/// so the last message is not silently discarded and TCP backpressure applies.
/// This is an operational bound, not an inferred retail gameplay cadence.
/// </summary>
internal sealed class WorldChatRateLimiter
{
    internal const int MessagesPerSecond = 4;
    private static readonly long s_windowTicks = Stopwatch.Frequency;
    private long _windowStart;
    private int _accepted;
    private bool _hasWindow;

    public bool TryAccept(long timestamp, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_hasWindow)
        {
            _hasWindow = true;
            _windowStart = timestamp;
        }
        else if (timestamp < _windowStart)
        {
            retryAfter = TimeSpan.FromMilliseconds(1);
            return false;
        }
        else if (timestamp - _windowStart >= s_windowTicks)
        {
            _windowStart = timestamp;
            _accepted = 0;
        }

        if (_accepted == MessagesPerSecond)
        {
            long remaining = s_windowTicks - (timestamp - _windowStart);
            retryAfter = TimeSpan.FromMilliseconds(Math.Max(1,
                (long)Math.Ceiling(remaining * 1000d / Stopwatch.Frequency)));
            return false;
        }

        _accepted++;
        return true;
    }
}
