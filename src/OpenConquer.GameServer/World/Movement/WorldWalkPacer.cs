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
    private long _lastJumpIntervalTicks;

    public bool TryAccept(uint clientTick, long serverTimestamp, bool twoTileMove = false) =>
        TryAcceptCore(clientTick, serverTimestamp, twoTileMove, jumpDistance: 0);

    /// <summary>Shares the monotonic replay and time budget with walking; the jump's
    /// distance controls the conservative minimum interval before and after it.</summary>
    public bool TryAcceptJump(uint clientTick, long serverTimestamp, int jumpDistance) =>
        jumpDistance is >= 1 and <= 16 &&
        TryAcceptCore(clientTick, serverTimestamp, twoTileMove: false, jumpDistance: jumpDistance);

    private bool TryAcceptCore(uint clientTick, long serverTimestamp, bool twoTileMove, int jumpDistance)
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
            long minimumTicks = Math.Max(_lastJumpIntervalTicks,
                jumpDistance > 0 ? checked(jumpDistance * s_minimumIntervalTicks) :
                twoTileMove || _lastTwoTileMove ? checked(2 * s_minimumIntervalTicks) : s_minimumIntervalTicks);
            if (serverTimestamp < _lastAcceptedTimestamp || serverTimestamp - _lastAcceptedTimestamp < minimumTicks)
            {
                return false;
            }
        }

        _lastAcceptedTimestamp = serverTimestamp;
        _lastAcceptedClientTick = clientTick;
        _lastTwoTileMove = twoTileMove;
        _lastJumpIntervalTicks = checked(jumpDistance * s_minimumIntervalTicks);
        _hasAccepted = true;
        return true;
    }
}
