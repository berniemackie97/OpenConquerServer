namespace OpenConquer.Transport.Connections;

/// <summary>
/// Provides higher level byte stream operations over transport connections.
/// </summary>
public static class TransportConnectionExtensions
{
    /// <summary>
    /// Receives exactly <paramref name="buffer"/>.Length bytes unless the peer closes its sending side first.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the complete buffer was filled
    /// <see langword="false"/> when EOF was reached first.
    /// </returns>
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
