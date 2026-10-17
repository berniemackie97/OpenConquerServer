namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// A reversible, generation-bound pre-transfer gate. It does not change residency,
/// persistence, visibility, or client state. Disposal releases the destination slot
/// and permits new source-map commands if the original owner still exists.
/// </summary>
internal sealed class WorldTransferReservation : IDisposable
{
    private readonly WorldRuntime _runtime;
    private WorldCharacterSnapshot? _sourceSnapshot;

    internal WorldTransferReservation(WorldRuntime runtime, WorldCharacterOwnership ownership,
        uint destinationMapId, ushort x, ushort y)
    {
        _runtime = runtime;
        SourceOwnership = ownership;
        DestinationMapId = destinationMapId;
        X = x;
        Y = y;
    }

    public WorldCharacterOwnership SourceOwnership { get; }
    public uint DestinationMapId { get; }
    public ushort X { get; }
    public ushort Y { get; }

    /// <summary>Committed source-map state after all previously admitted ordinary commands.</summary>
    public WorldCharacterSnapshot SourceSnapshot => _sourceSnapshot
        ?? throw new InvalidOperationException("The source-partition barrier has not completed.");

    /// <summary>A live lease can be revoked by session removal or runtime shutdown.</summary>
    public bool IsActive => _runtime.IsTransferReservationActive(this);

    internal void Complete(WorldCharacterSnapshot snapshot)
    {
        if (_sourceSnapshot is not null || snapshot.Ownership != SourceOwnership)
        {
            throw new InvalidOperationException("Transfer reservation received an invalid source snapshot.");
        }

        _sourceSnapshot = snapshot;
    }

    public void Dispose() => _runtime.CancelTransferReservation(this);
}
