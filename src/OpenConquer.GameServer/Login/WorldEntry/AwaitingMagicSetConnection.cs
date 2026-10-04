using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after weapon-skill hydration and before the magic-skill bootstrap rung.
/// </summary>
internal sealed class AwaitingMagicSetConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingMagicSetConnection(ExistingCharacterGameConnection connection, GameMapEntryDefinition map, CharacterItemSet itemSet,
        CharacterSocialRelationSet socialRelationSet, CharacterWeaponSkillSet weaponSkillSet)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(itemSet);
        ArgumentNullException.ThrowIfNull(socialRelationSet);
        ArgumentNullException.ThrowIfNull(weaponSkillSet);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The magic-set connection map does not match the character's persisted map.", nameof(map));
        }

        uint characterId = connection.Profile.Identity.CharacterId;

        if (itemSet.CharacterId != characterId)
        {
            throw new ArgumentException("The magic-set connection item set belongs to a different character.", nameof(itemSet));
        }

        if (socialRelationSet.CharacterId != characterId)
        {
            throw new ArgumentException("The magic-set connection social-relation set belongs to a different character.", nameof(socialRelationSet));
        }

        if (weaponSkillSet.CharacterId != characterId)
        {
            throw new ArgumentException("The magic-set connection weapon-skill set belongs to a different character.", nameof(weaponSkillSet));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
        ItemSet = itemSet;
        SocialRelationSet = socialRelationSet;
        WeaponSkillSet = weaponSkillSet;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }
    public CharacterItemSet ItemSet { get; }
    public CharacterSocialRelationSet SocialRelationSet { get; }
    public CharacterWeaponSkillSet WeaponSkillSet { get; }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The magic-set connection has already been transferred or disposed.");
    }

    public async ValueTask DisposeAsync()
    {
        ExistingCharacterGameConnection? connection = Interlocked.Exchange(ref _connection, null);

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
