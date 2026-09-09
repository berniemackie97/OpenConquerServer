namespace OpenConquer.AccountServer.Login.Workers;

internal sealed class LoginConnectionWorkerPoolConfiguration
{
    private static readonly TimeSpan s_maximumConnectionTimeout = TimeSpan.FromMilliseconds(0xFFFF_FFFE);

    public LoginConnectionWorkerPoolConfiguration(int workerCount, TimeSpan connectionTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);

        if (connectionTimeout <= TimeSpan.Zero || connectionTimeout > s_maximumConnectionTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout), $"The login connection timeout must be greater than zero and no greater than {s_maximumConnectionTimeout}.");
        }

        WorkerCount = workerCount;
        ConnectionTimeout = connectionTimeout;
    }

    public int WorkerCount { get; }
    public TimeSpan ConnectionTimeout { get; }
}
