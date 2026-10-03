namespace OpenConquer.GameServer.World.Presence;

internal interface ICharacterPresenceReader
{
    bool IsOnline(uint characterId);
}
