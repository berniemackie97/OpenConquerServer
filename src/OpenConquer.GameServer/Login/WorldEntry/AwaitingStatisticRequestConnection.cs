using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after the silent-info report and before the native statistics request.
/// </summary>
internal sealed class AwaitingStatisticRequestConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingStatisticRequestConnection(ExistingCharacterGameConnection connection, GameMapEntryDefinition map, CharacterItemSet itemSet,
        CharacterSocialRelationSet socialRelationSet, CharacterWeaponSkillSet weaponSkillSet, CharacterMagicSet magicSet,
        CharacterSyndicateState syndicateState, uint clientReportedSilentDataChecksum, uint clientReportedSilentDataVersion)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(itemSet);
        ArgumentNullException.ThrowIfNull(socialRelationSet);
        ArgumentNullException.ThrowIfNull(weaponSkillSet);
        ArgumentNullException.ThrowIfNull(magicSet);
        ArgumentNullException.ThrowIfNull(syndicateState);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The statistics-request connection map does not match the character's persisted map.", nameof(map));
        }

        uint characterId = connection.Profile.Identity.CharacterId;

        if (itemSet.CharacterId != characterId)
        {
            throw new ArgumentException("The statistics-request connection item set belongs to a different character.", nameof(itemSet));
        }

        if (socialRelationSet.CharacterId != characterId)
        {
            throw new ArgumentException("The statistics-request connection social-relation set belongs to a different character.", nameof(socialRelationSet));
        }

        if (weaponSkillSet.CharacterId != characterId)
        {
            throw new ArgumentException("The statistics-request connection weapon-skill set belongs to a different character.", nameof(weaponSkillSet));
        }

        if (magicSet.CharacterId != characterId)
        {
            throw new ArgumentException("The statistics-request connection magic set belongs to a different character.", nameof(magicSet));
        }

        if (syndicateState.CharacterId != characterId)
        {
            throw new ArgumentException("The statistics-request connection syndicate state belongs to a different character.", nameof(syndicateState));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
        ItemSet = itemSet;
        SocialRelationSet = socialRelationSet;
        WeaponSkillSet = weaponSkillSet;
        MagicSet = magicSet;
        SyndicateState = syndicateState;
        ClientReportedSilentDataChecksum = clientReportedSilentDataChecksum;
        ClientReportedSilentDataVersion = clientReportedSilentDataVersion;
    }

    public CharacterLoginProfile Profile
    {
        get;
    }

    public GameMapEntryDefinition Map
    {
        get;
    }

    public CharacterItemSet ItemSet
    {
        get;
    }

    public CharacterSocialRelationSet SocialRelationSet
    {
        get;
    }

    public CharacterWeaponSkillSet WeaponSkillSet
    {
        get;
    }

    public CharacterMagicSet MagicSet
    {
        get;
    }

    public CharacterSyndicateState SyndicateState
    {
        get;
    }

    public uint ClientReportedSilentDataChecksum
    {
        get;
    }

    public uint ClientReportedSilentDataVersion
    {
        get;
    }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The statistics-request connection has already been transferred or disposed.");
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
