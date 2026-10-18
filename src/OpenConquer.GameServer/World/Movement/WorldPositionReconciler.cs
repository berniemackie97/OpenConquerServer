using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// Remembers the tiles the server recently accepted for one session, so position reports the client sent
/// before an echo arrived aren't mistaken for desync. Only the session's reader uses it, and it never moves
/// the character.
/// </summary>
internal sealed class WorldPositionReconciler
{
    private const int HistoryCapacity = 64;
    private const int DivergentReportsBeforeCorrection = 3;
    private static readonly long s_minimumCorrectionInterval = Stopwatch.Frequency;
    // A per-session cap on corrections keeps predict-and-correct working while disconnecting clients that abuse
    // it, rather than leaving them out of place.
    internal const int MaximumRejectedCorrectionsPerSecond = 32;
    private static readonly long s_rejectedMovementWindow = Stopwatch.Frequency;

    private readonly Tile[] _history = new Tile[HistoryCapacity];
    private int _next;
    private int _count;
    private int _divergentReports;
    private long _lastCorrectionTimestamp;
    private bool _hasCorrected;
    private long _rejectedMovementWindowStart;
    private int _rejectedMovementCount;

    public WorldPositionReconciler(ushort startX, ushort startY)
    {
        RecordAuthorized(startX, startY);
    }

    public void RecordAuthorized(ushort x, ushort y)
    {
        Tile tile = new(x, y);
        int previous = (_next + HistoryCapacity - 1) % HistoryCapacity;

        if (_count != 0 && _history[previous] == tile)
        {
            return;
        }

        _history[_next] = tile;
        _next = (_next + 1) % HistoryCapacity;
        _count = Math.Min(_count + 1, HistoryCapacity);
    }

    /// <summary>
    /// Reserves an immediate correction for a refused move. The 5517 client animates walks and jumps before the
    /// server echoes them, so withholding the echo isn't enough to put it back.
    /// </summary>
    public bool TryReserveRejectedMovementCorrection(long serverTimestamp)
    {
        if (_rejectedMovementCount == 0 || serverTimestamp < _rejectedMovementWindowStart ||
            serverTimestamp - _rejectedMovementWindowStart >= s_rejectedMovementWindow)
        {
            _rejectedMovementWindowStart = serverTimestamp;
            _rejectedMovementCount = 1;
            return true;
        }

        if (_rejectedMovementCount >= MaximumRejectedCorrectionsPerSecond)
        {
            return false;
        }

        _rejectedMovementCount++;
        return true;
    }

    /// <summary>
    /// True only after repeated reports that match no known tile, and after the cooldown. A tile in the recent
    /// history counts as a report in flight, however far away it is now.
    /// </summary>
    public bool ShouldCorrect(ushort clientX, ushort clientY, long serverTimestamp)
    {
        Tile reported = new(clientX, clientY);

        for (int i = 0; i < _count; i++)
        {
            if (_history[i] == reported)
            {
                _divergentReports = 0;
                return false;
            }
        }

        _divergentReports = Math.Min(_divergentReports + 1, DivergentReportsBeforeCorrection);

        if (_divergentReports != DivergentReportsBeforeCorrection)
        {
            return false;
        }

        if (_hasCorrected && (serverTimestamp < _lastCorrectionTimestamp ||
                              serverTimestamp - _lastCorrectionTimestamp < s_minimumCorrectionInterval))
        {
            return false;
        }

        _divergentReports = 0;
        _lastCorrectionTimestamp = serverTimestamp;
        _hasCorrected = true;
        return true;
    }

    private readonly record struct Tile(ushort X, ushort Y);
}
