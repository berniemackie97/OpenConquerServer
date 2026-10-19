using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// One durable character-row write generation. The token is a database CAS predicate,
/// not authentication or a process-wide lease. Acquiring a newer generation revokes it.
/// </summary>
public sealed class CharacterLocationWriteAuthority
{
    public CharacterLocationWriteAuthority(uint accountId, uint characterId, Guid token,
        PersistedCharacterLocation snapshot)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        if (token == Guid.Empty)
        {
            throw new ArgumentException("A durable writer generation requires a nonempty token.", nameof(token));
        }
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.WriterToken != token)
        {
            throw new ArgumentException("The observed generation does not match the acquired writer token.", nameof(snapshot));
        }
        AccountId = accountId;
        CharacterId = characterId;
        Token = token;
    }

    public uint AccountId { get; }
    public uint CharacterId { get; }
    public Guid Token { get; }
    public PersistedCharacterLocation Snapshot { get; }
}
