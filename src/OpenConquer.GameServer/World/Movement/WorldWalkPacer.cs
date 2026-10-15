using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// One-session monotonic walking cadence and replay guard. Not shared between characters.
/// Must be called by the session's single sequential packet reader.
/// </summary>
internal sealed class WorldWalkPacer
{
    private static readonly long s_minimumIntervalTicks = Math.Max(1, (Stopwatch.Frequency + 7) / 8);

    private long _lastAcceptedTimestamp;
    private uint _lastAcceptedClientTick;
    private bool _hasAccepted;
    private bool _lastTwoTileMove;

    public bool TryAccept(uint clientTick, long serverTimestamp, bool twoTileMove = false)
    {
        if (_hasAccepted)
        {
            uint clientDelta = unchecked(clientTick - _lastAcceptedClientTick);

            // Unsigned arithmetic permits a real uint32 wrap but rejects duplicates and
            // backwards movement ticks. The client tick never determines server cadence.
            if (clientDelta == 0 || clientDelta >= 0x8000_0000u)
            {
                return false;
            }

            // Until mounted-state authorization is modeled, a two-tile step
            // cannot double the unmounted walking speed by using the same
            // admission interval. This is a server safety limit, not retail timing.
            long minimumTicks = twoTileMove || _lastTwoTileMove
                ? checked(2 * s_minimumIntervalTicks)
                : s_minimumIntervalTicks;
            if (serverTimestamp < _lastAcceptedTimestamp || serverTimestamp - _lastAcceptedTimestamp < minimumTicks)
            {
                return false;
            }
        }

        _lastAcceptedTimestamp = serverTimestamp;
        _lastAcceptedClientTick = clientTick;
        _lastTwoTileMove = twoTileMove;
        _hasAccepted = true;
        return true;
    }
}
