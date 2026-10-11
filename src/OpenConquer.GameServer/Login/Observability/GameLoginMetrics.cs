using System.Diagnostics.Metrics;

namespace OpenConquer.GameServer.Login.Observability;

internal sealed class GameLoginMetrics : IDisposable
{
    public const string MeterName = "OpenConquer.GameServer.Login";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _sourceRejected;
    private readonly Counter<long> _capacityRejected;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _notAdmitted;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _timedOut;
    private readonly UpDownCounter<long> _worldSessions;
    private readonly Histogram<double> _loginDuration;

    public GameLoginMetrics()
    {
        _accepted = _meter.CreateCounter<long>("game.login.accepted");
        _sourceRejected = _meter.CreateCounter<long>("game.login.source_rejected");
        _capacityRejected = _meter.CreateCounter<long>("game.login.capacity_rejected");
        _completed = _meter.CreateCounter<long>("game.login.completed");
        _notAdmitted = _meter.CreateCounter<long>("game.login.not_admitted");
        _failed = _meter.CreateCounter<long>("game.login.failed");
        _timedOut = _meter.CreateCounter<long>("game.login.timed_out");
        _worldSessions = _meter.CreateUpDownCounter<long>("game.login.world_sessions");
        _loginDuration = _meter.CreateHistogram<double>("game.login.duration", "ms");
    }

    public void RecordAccepted() => _accepted.Add(1);
    public void RecordSourceRejected() => _sourceRejected.Add(1);
    public void RecordCapacityRejected() => _capacityRejected.Add(1);

    public void RecordCompleted(double durationMilliseconds)
    {
        _completed.Add(1);
        _loginDuration.Record(durationMilliseconds);
    }

    public void RecordNotAdmitted(double durationMilliseconds)
    {
        _notAdmitted.Add(1);
        _loginDuration.Record(durationMilliseconds);
    }

    public void RecordFailure(double durationMilliseconds)
    {
        _failed.Add(1);
        _loginDuration.Record(durationMilliseconds);
    }

    public void RecordTimeout(double durationMilliseconds)
    {
        _timedOut.Add(1);
        _loginDuration.Record(durationMilliseconds);
    }

    public void RecordSessionStarted() => _worldSessions.Add(1);
    public void RecordSessionEnded() => _worldSessions.Add(-1);

    public void Dispose() => _meter.Dispose();
}
