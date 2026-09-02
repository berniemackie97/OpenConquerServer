using System.Buffers;
using System.IO.Pipelines;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Transfers bytes from a caller-owned pipeline to an established transport
/// connection while preserving byte order and pipeline backpressure.
/// </summary>
public static class TransportConnectionOutput
{
    public static async Task PumpAsync(ITransportConnection connection, PipeReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(reader);

        Exception? completionException = null;

        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition consumed = buffer.Start;

                try
                {
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    foreach (ReadOnlyMemory<byte> segment in buffer)
                    {
                        if (!segment.IsEmpty)
                        {
                            await connection.SendAsync(segment, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    consumed = buffer.End;

                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    reader.AdvanceTo(consumed);
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
            await reader.CompleteAsync(completionException).ConfigureAwait(false);
        }
    }
}
