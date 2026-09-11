namespace OpenConquer.AccountServer.Hosting;

internal sealed class FatalBackgroundServiceFailureState
{
    private Exception? _failure;

    public Exception? Failure => Volatile.Read(ref _failure);

    public void Record(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Interlocked.CompareExchange(ref _failure, failure, null);
    }
}
