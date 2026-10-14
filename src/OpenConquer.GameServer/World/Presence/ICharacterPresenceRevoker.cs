namespace OpenConquer.GameServer.World.Presence;

internal interface ICharacterPresenceRevoker
{
    /// <summary>
    /// Ends the character's presence in this realm, which disconnects its session. Returns
    /// <see langword="false"/> when the character is not present.
    /// </summary>
    bool Revoke(uint characterId);
}
