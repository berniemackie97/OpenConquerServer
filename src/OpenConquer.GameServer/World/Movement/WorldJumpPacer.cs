using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// One-session jump cadence and replay guard, independent of walking.
/// </summary>
/// <remarks>
/// Shape follows TQ 15.10 <c>CUser__CheckJumpTimeStamp</c> (MsgServer <c>0x4168DF</c>): its own ring of four
/// samples, the first four pass, then the mean of the last three intervals must be at least 643 ms. The TQ server
/// fills the ring from client timestamps; here the ring holds server-clock arrival times of accepted jumps, so a
/// client tick never sets the cadence. The jump's client timestamp is only a replay guard within the jump stream.
/// Must be called by the session's single sequential packet reader.
/// </remarks>
internal sealed class WorldJumpPacer
{
    internal const int RingSize = 4;
    internal const int MinimumMeanIntervalMilliseconds = 643;

    private static readonly long s_minimumWindowTicks = checked(
        (RingSize - 1) * MinimumMeanIntervalMilliseconds * Stopwatch.Frequency / 1000);

    private readonly long[] _accepted = new long[RingSize];
    private int _count;
    private int _oldest;
    private uint _lastClientTimestamp;

    public bool TryAccept(uint clientTimestamp, long serverTimestamp)
    {
        if (_count != 0)
        {
            // Unsigned arithmetic permits a real uint32 wrap but rejects duplicate and backwards timestamps.
            uint clientDelta = unchecked(clientTimestamp - _lastClientTimestamp);
            if (clientDelta == 0 || clientDelta >= 0x8000_0000u)
            {
                return false;
            }

            long newest = _accepted[(_oldest + _count - 1) % RingSize];
            if (serverTimestamp < newest)
            {
                return false;
            }
        }

        if (_count < RingSize)
        {
            _accepted[(_oldest + _count) % RingSize] = serverTimestamp;
            _count++;
        }
        else
        {
            // After the oldest sample leaves, the window spans three intervals ending at this jump.
            long windowStart = _accepted[(_oldest + 1) % RingSize];
            if (serverTimestamp - windowStart < s_minimumWindowTicks)
            {
                return false;
            }

            _accepted[_oldest] = serverTimestamp;
            _oldest = (_oldest + 1) % RingSize;
        }

        _lastClientTimestamp = clientTimestamp;
        return true;
    }
}
