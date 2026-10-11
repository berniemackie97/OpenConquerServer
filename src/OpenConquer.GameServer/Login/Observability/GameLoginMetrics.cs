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
    private readonly Counter<long> _failed;
    private readonly Counter<long> _timedOut;
    private readonly UpDownCounter<long> _worldSessions;
    private readonly Histogram<double> _loginDuration;

    public GameLoginMetrics()
    {
        _accepted = _meter.CreateCounter<long>("openconquer.game_server.login.admission.accepted");
        _sourceRejected = _meter.CreateCounter<long>("openconquer.game_server.login.admission.source_rejections");
        _capacityRejected = _meter.CreateCounter<long>("openconquer.game_server.login.admission.capacity_rejections");
        _completed = _meter.CreateCounter<long>("openconquer.game_server.login.connection.completions");
        _failed = _meter.CreateCounter<long>("openconquer.game_server.login.connection.failures");
        _timedOut = _meter.CreateCounter<long>("openconquer.game_server.login.connection.timeouts");
        _worldSessions = _meter.CreateUpDownCounter<long>("openconquer.game_server.world_sessions.active");
        _loginDuration = _meter.CreateHistogram<double>("openconquer.game_server.login.connection.duration", "ms");
    }

    public void RecordAccepted() => _accepted.Add(1);
    public void RecordSourceRejected() => _sourceRejected.Add(1);
    public void RecordCapacityRejected() => _capacityRejected.Add(1);

    public void RecordCompleted(double durationMilliseconds)
    {
        _completed.Add(1);
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
