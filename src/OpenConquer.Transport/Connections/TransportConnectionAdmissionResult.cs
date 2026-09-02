namespace OpenConquer.Transport.Connections;

/// <summary>
/// Describes the outcome of attempting to transfer ownership of an established
/// transport connection into the admission queue.
/// </summary>
public enum TransportConnectionAdmissionResult
{
    Admitted,
    CapacityExhausted,
    Completed,
}
