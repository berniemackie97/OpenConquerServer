using System.Diagnostics;

namespace OpenConquer.GameServer.World.Movement;

/// <summary>
/// Single-reader, bounded record of server-authorized tiles for native position reports.
/// History accommodates client reports already in flight while earlier movement echoes arrive.
/// This class never changes an authoritative world position.
/// </summary>
internal sealed class WorldPositionReconciler
{
    private const int HistoryCapacity = 64;
    private const int DivergentReportsBeforeCorrection = 3;
    private static readonly long s_minimumCorrectionInterval = Stopwatch.Frequency;

    private readonly Tile[] _history = new Tile[HistoryCapacity];
    private int _next;
    private int _count;
    private int _divergentReports;
    private long _lastCorrectionTimestamp;
    private bool _hasCorrected;

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
    /// Returns true only after repeated, unmatched reports and the server-clock cooldown.
    /// A known historical tile is an in-flight report, regardless of its distance from now.
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
