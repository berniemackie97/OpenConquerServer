namespace OpenConquer.GameServer.World.Presence;

internal interface ICharacterPresenceRegistrar
{
    ICharacterPresenceLease Register(uint characterId);
}
