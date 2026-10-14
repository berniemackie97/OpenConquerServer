using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// Resource-abuse limit for cosmetic, network-fanned-out player actions; not a movement
/// cadence rule and not asserted to be a retail 5517 gameplay timing constant.
/// A single authenticated gameplay reader owns this state.
/// </summary>
internal sealed class WorldAvatarActionLimiter
{
    internal const int MaximumActionsPerWindow = 8;
    private static readonly long s_windowTicks = Stopwatch.Frequency;
    private long _windowStart;
    private int _actions;
    private bool _hasWindow;

    public bool TryAccept(long serverTimestamp)
    {
        if (!_hasWindow)
        {
            _hasWindow = true;
            _windowStart = serverTimestamp;
        }
        else if (serverTimestamp < _windowStart)
        {
            return false;
        }
        else if (serverTimestamp - _windowStart >= s_windowTicks)
        {
            _windowStart = serverTimestamp;
            _actions = 0;
        }

        if (_actions >= MaximumActionsPerWindow)
        {
            return false;
        }

        _actions++;
        return true;
    }
}
