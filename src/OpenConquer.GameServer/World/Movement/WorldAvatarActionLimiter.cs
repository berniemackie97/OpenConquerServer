using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// Per-session limit on observer-fanned cosmetic actions. The serialized session reader
/// waits on exhaustion instead of dropping the final state or creating a second queue.
/// This is an operational resource bound, not a retail 5517 animation-cadence rule.
/// </summary>
internal sealed class WorldAvatarActionLimiter
{
    internal const int MaximumActionsPerWindow = 8;
    private static readonly long s_windowTicks = Stopwatch.Frequency;
    private long _windowStart;
    private int _actions;
    private bool _hasWindow;

    public bool TryAccept(long serverTimestamp, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_hasWindow)
        {
            _hasWindow = true;
            _windowStart = serverTimestamp;
        }
        else if (serverTimestamp < _windowStart)
        {
            // A monotonic-clock regression must never move our accounting backwards.
            retryAfter = TimeSpan.FromMilliseconds(1);
            return false;
        }
        else if (serverTimestamp - _windowStart >= s_windowTicks)
        {
            _windowStart = serverTimestamp;
            _actions = 0;
        }

        if (_actions >= MaximumActionsPerWindow)
        {
            long remainingTicks = s_windowTicks - (serverTimestamp - _windowStart);
            retryAfter = TimeSpan.FromMilliseconds(Math.Max(1,
                (long)Math.Ceiling(remainingTicks * 1000d / Stopwatch.Frequency)));
            return false;
        }

        _actions++;
        return true;
    }
}
