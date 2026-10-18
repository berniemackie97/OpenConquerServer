using OpenConquer.Application.Characters.Location;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Pairs an immutable persisted-source observation with a revocable, generation-fenced
/// destination reservation. No write, residency change, or network acknowledgement occurs.
/// </summary>
internal sealed class WorldPortalPersistedLocationReservation : IDisposable
{
    private readonly WorldTransferReservation _reservation;

    internal WorldPortalPersistedLocationReservation(WorldTransferReservation reservation, PersistedCharacterLocation persistedSource)
    {
        _reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
        PersistedSource = persistedSource ?? throw new ArgumentNullException(nameof(persistedSource));
        if (PersistedSource.Location.MapId != reservation.SourceOwnership.MapId)
        {
            throw new ArgumentException("Persisted source map must match the reserved source world.", nameof(persistedSource));
        }
    }

    public PersistedCharacterLocation PersistedSource { get; }
    public WorldCharacterOwnership SourceOwnership => _reservation.SourceOwnership;
    public WorldCharacterSnapshot SourceSnapshot => _reservation.SourceSnapshot;
    public uint DestinationMapId => _reservation.DestinationMapId;
    public ushort DestinationX => _reservation.X;
    public ushort DestinationY => _reservation.Y;
    public bool IsActive => _reservation.IsActive;

    public void Dispose() => _reservation.Dispose();
}
