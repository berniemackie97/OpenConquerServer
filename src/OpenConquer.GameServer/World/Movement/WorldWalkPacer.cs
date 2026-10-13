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

    public bool TryAccept(uint clientTick, long serverTimestamp)
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

            if (serverTimestamp < _lastAcceptedTimestamp || serverTimestamp - _lastAcceptedTimestamp < s_minimumIntervalTicks)
            {
                return false;
            }
        }

        _lastAcceptedTimestamp = serverTimestamp;
        _lastAcceptedClientTick = clientTick;
        _hasAccepted = true;
        return true;
    }
}
