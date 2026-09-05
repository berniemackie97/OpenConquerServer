namespace OpenConquer.Transport.Connections;

public static class TransportConnectionExtensions
{
    public static async ValueTask<bool> TryReceiveExactlyAsync(this ITransportConnection connection, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        int received = 0;

        while (received < buffer.Length)
        {
            int count = await connection.ReceiveAsync(buffer[received..], cancellationToken).ConfigureAwait(false);

            if (count == 0)
            {
                return false;
            }

            if ((uint)count > (uint)(buffer.Length - received))
            {
                throw new InvalidOperationException($"Transport connection returned an invalid receive count of {count}.");
            }

            received += count;
        }

        return true;
    }
}
