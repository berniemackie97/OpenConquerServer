namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Opaque proof that a world owner was fenced before attempting a durable portal write.
/// No public release path exists: only successful residency handoff or world shutdown
/// can end this seal. It must never be handed to untrusted network code.
/// </summary>
internal sealed class WorldDurableTransferSeal(WorldTransferReservation reservation)
{
    internal WorldTransferReservation Reservation { get; } = reservation ??
        throw new ArgumentNullException(nameof(reservation));
}
