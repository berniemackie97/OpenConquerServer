using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Connection and loaded state for one existing-character login while the client works through its bootstrap requests.
/// Only the login processor holds it, so it needs no locking.
/// </summary>
internal sealed class ExistingCharacterLoginContext : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public ExistingCharacterLoginContext(ExistingCharacterGameConnection connection, WorldMapContent map)
    {
        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The login map does not match the character's persisted map.", nameof(map));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
    }

    public ExistingCharacterGameConnection Connection =>
        _connection ?? throw new InvalidOperationException("The login connection has already been handed to world admission or disposed.");

    public CharacterLoginProfile Profile { get; }
    public uint CharacterId => Profile.Identity.CharacterId;
    public WorldMapContent Map { get; }

    public CharacterItemSet? ItemSet { get; set => field = RequireCharacter(value, value?.CharacterId, "item set"); }
    public CharacterSocialRelationSet? SocialRelationSet { get; set => field = RequireCharacter(value, value?.CharacterId, "social-relation set"); }
    public CharacterWeaponSkillSet? WeaponSkillSet { get; set => field = RequireCharacter(value, value?.CharacterId, "weapon-skill set"); }
    public CharacterMagicSet? MagicSet { get; set => field = RequireCharacter(value, value?.CharacterId, "magic set"); }
    public CharacterSyndicateState? SyndicateState { get; set => field = RequireCharacter(value, value?.CharacterId, "syndicate state"); }
    public uint? ClientReportedSilentDataChecksum { get; set; }
    public uint? ClientReportedSilentDataVersion { get; set; }

    /// <summary>
    /// Builds the completed bootstrap from every step's state and hands the connection to it.
    /// If this throws, the context still owns the connection.
    /// </summary>
    public ExistingCharacterBootstrapCompleteConnection Complete()
    {
        ExistingCharacterBootstrapCompleteConnection completed = new(Connection, Map,
            ItemSet ?? throw StepMissing("item-set"),
            SocialRelationSet ?? throw StepMissing("friend-list"),
            WeaponSkillSet ?? throw StepMissing("weapon-skill"),
            MagicSet ?? throw StepMissing("magic-set"),
            SyndicateState ?? throw StepMissing("syndicate"),
            ClientReportedSilentDataChecksum ?? throw StepMissing("silent-info"),
            ClientReportedSilentDataVersion ?? throw StepMissing("silent-info"));

        _connection = null;
        return completed;
    }

    public ValueTask DisposeAsync()
    {
        ExistingCharacterGameConnection? connection = _connection;
        _connection = null;

        return connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private T? RequireCharacter<T>(T? state, uint? characterId, string name)
        where T : class
    {
        if (characterId is { } owner && owner != CharacterId)
        {
            throw new ArgumentException($"The login {name} belongs to character {owner}, not {CharacterId}.");
        }

        return state;
    }

    private static InvalidOperationException StepMissing(string step) => new($"The login cannot complete before its {step} step.");
}
