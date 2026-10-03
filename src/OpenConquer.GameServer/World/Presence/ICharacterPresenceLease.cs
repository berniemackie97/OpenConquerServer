namespace OpenConquer.GameServer.World.Presence;

internal interface ICharacterPresenceLease : IDisposable
{
    uint CharacterId { get; }
    bool IsRevoked { get; }
    CancellationToken RevocationToken { get; }
}
