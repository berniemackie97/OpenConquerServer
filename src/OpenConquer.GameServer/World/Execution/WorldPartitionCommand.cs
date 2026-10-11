namespace OpenConquer.GameServer.World.Execution;

internal interface IWorldPartitionCommand
{
    void Execute(MapInstance map);
    void Reject(Exception exception);
}

internal sealed class WorldPartitionCommand<TResult> : IWorldPartitionCommand
{
    private readonly Func<MapInstance, TResult> _operation;
    private readonly Action<Exception>? _onFailure;
    private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    public WorldPartitionCommand(Func<MapInstance, TResult> operation, Action<Exception>? onFailure = null)
    {
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _onFailure = onFailure;
    }

    public Task<TResult> Completion => _completion.Task;

    public void Execute(MapInstance map)
    {
        ArgumentNullException.ThrowIfNull(map);

        try
        {
            TResult result = _operation(map);

            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _completion.TrySetResult(result);
            }
        }
        catch (Exception exception)
        {
            Reject(exception);
        }
    }

    public void Reject(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        try
        {
            _onFailure?.Invoke(exception);
            _completion.TrySetException(exception);
        }
        catch (Exception cleanupException)
        {
            _completion.TrySetException(new AggregateException("World command failure handling also failed.", exception, cleanupException));
        }
    }
}
