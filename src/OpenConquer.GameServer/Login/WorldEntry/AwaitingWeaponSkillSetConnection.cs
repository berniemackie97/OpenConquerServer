using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after social-relation hydration and before the weapon-skill bootstrap rung.
/// </summary>
internal sealed class AwaitingWeaponSkillSetConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingWeaponSkillSetConnection(ExistingCharacterGameConnection connection, GameMapEntryDefinition map, CharacterItemSet itemSet, CharacterSocialRelationSet socialRelationSet)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(itemSet);
        ArgumentNullException.ThrowIfNull(socialRelationSet);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The weapon-skill connection map does not match the character's persisted map.", nameof(map));
        }

        if (itemSet.CharacterId != connection.Profile.Identity.CharacterId)
        {
            throw new ArgumentException("The weapon-skill connection item set belongs to a different character.", nameof(itemSet));
        }

        if (socialRelationSet.CharacterId != connection.Profile.Identity.CharacterId)
        {
            throw new ArgumentException("The weapon-skill connection social-relation set belongs to a different character.", nameof(socialRelationSet));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
        ItemSet = itemSet;
        SocialRelationSet = socialRelationSet;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }
    public CharacterItemSet ItemSet { get; }
    public CharacterSocialRelationSet SocialRelationSet { get; }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The weapon-skill connection has already been transferred or disposed.");
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
