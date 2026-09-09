using System.Diagnostics.Metrics;

namespace OpenConquer.AccountServer.Login.Observability;

internal sealed class LoginRuntimeMetrics : IDisposable
{
    private readonly Meter _meter = new("OpenConquer.AccountServer.Login");
    private readonly Counter<long> _capacityRejections;
    private readonly Counter<long> _rejectionDisposalFailures;
    private readonly Counter<long> _connectionTimeouts;
    private readonly Counter<long> _connectionFailures;

    public LoginRuntimeMetrics()
    {
        _capacityRejections = _meter.CreateCounter<long>("login.admission.capacity_rejections", "{connection}", "Connections rejected because the bounded login admission queue was full.");
        _rejectionDisposalFailures = _meter.CreateCounter<long>("login.admission.rejection_disposal_failures", "{connection}", "Rejected connections whose disposal failed.");
        _connectionTimeouts = _meter.CreateCounter<long>("login.connection.timeouts", "{connection}", "Login connections terminated by the whole-connection timeout.");
        _connectionFailures = _meter.CreateCounter<long>("login.connection.failures", "{connection}", "Login connections that failed during processing or cleanup.");
    }

    public void RecordCapacityRejection() => _capacityRejections.Add(1);
    public void RecordRejectionDisposalFailure() => _rejectionDisposalFailures.Add(1);
    public void RecordConnectionTimeout() => _connectionTimeouts.Add(1);
    public void RecordConnectionFailure() => _connectionFailures.Add(1);
    public void Dispose() => _meter.Dispose();
}
