using System.IO.Pipelines;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Transfers bytes from an established transport connection into a caller owned pipeline while preserving transport ordering and pipeline backpressure.
/// </summary>
public static class TransportConnectionInput
{
    public static async Task PumpAsync(ITransportConnection connection, PipeWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(writer);

        Exception? completionException = null;

        try
        {
            while (true)
            {
                Memory<byte> destination = writer.GetMemory(sizeHint: 1);

                int received = await connection.ReceiveAsync(destination, cancellationToken).ConfigureAwait(false);

                if ((uint)received > (uint)destination.Length)
                {
                    throw new InvalidOperationException($"Transport connection returned an invalid receive count of {received}.");
                }

                if (received == 0)
                {
                    return;
                }

                writer.Advance(received);

                FlushResult flush = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (flush.IsCanceled || flush.IsCompleted)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            completionException = exception;
            throw;
        }
        finally
        {
            await writer.CompleteAsync(completionException).ConfigureAwait(false);
        }
    }
}
