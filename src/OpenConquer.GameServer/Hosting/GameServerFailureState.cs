namespace OpenConquer.GameServer.Hosting;

internal sealed class GameServerFailureState
{
    private readonly Lock _gate = new();
    private Exception? _failure;

    public Exception? Failure => Volatile.Read(ref _failure);

    public void Record(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_gate)
        {
            _failure ??= failure;
        }
    }

    public void EnterWorld(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        lock (_gate)
        {
            if (_failure is not null)
            {
                throw new InvalidOperationException("World admission is closed after a fatal GameServer failure.", _failure);
            }

            activate();
        }
    }
}
