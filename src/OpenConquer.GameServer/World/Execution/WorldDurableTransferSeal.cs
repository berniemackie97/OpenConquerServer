namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Opaque proof that a world owner was fenced before attempting a durable portal write.
/// Successful residency handoff or world shutdown ends it. A connected seal can also be
/// abandoned before cutover, after which its session must close. It must never be handed
/// to untrusted network code.
/// </summary>
internal sealed class WorldDurableTransferSeal(WorldTransferReservation reservation)
{
    internal WorldTransferReservation Reservation { get; } = reservation ??
        throw new ArgumentNullException(nameof(reservation));
}
