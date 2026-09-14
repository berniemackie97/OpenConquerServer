namespace OpenConquer.Domain.Characters;

public static class CharacterIdentityPolicy
{
    public const uint FirstPlayerEntityId = 1_000_000;

    public static bool IsPlayerEntityId(uint characterId)
    {
        return characterId >= FirstPlayerEntityId;
    }
}
