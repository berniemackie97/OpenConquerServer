using OpenConquer.Application.Characters.Login;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Maps resolved existing-character state onto the native 5517 login packet contracts and established compatibility policy.
/// </summary>
internal static class ExistingCharacterBootstrapPacketFactory
{
    private const ushort GameEntranceChannel = 0x835;
    private const uint PreRebirthLevelScale = 100_000;
    private const string AcceptedMessage = "ANSWER_OK";
    private const string NoSpouseName = "None";
    private const byte LegacyAutoAllotCompatibilityValue = 1;

    private static readonly GameTalkPacket1004 s_accepted = new(color: 0, channel: GameEntranceChannel, style: 0, identity: 0, sender: string.Empty, recipient: string.Empty, suffix: string.Empty, message: AcceptedMessage);
    private static readonly GameLoginHistoryPacket2078 s_loginHistory = new(lastLoginTimestamp: 0, locationWarningFlag: 0, lastLoginLocation: string.Empty);
    private static readonly GameServerStatePacket2079 s_serverState = new(state: 0);

    public static GameTalkPacket1004 Accepted => s_accepted;
    public static GameLoginHistoryPacket2078 LoginHistory => s_loginHistory;
    public static GameServerStatePacket2079 ServerState => s_serverState;

    public static GameUserInfoPacket1006 CreateUserInfo(CharacterLoginProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new GameUserInfoPacket1006(profile.Identity.Name, NoSpouseName)
        {
            EntityId = profile.Identity.CharacterId,
            TransformLookSourceId = 0,
            PackedAppearance = profile.Appearance.Composite,
            HairComposite = profile.Appearance.Hair,
            Silver = profile.Economy.Silver,
            ConquerPoints = profile.Economy.ConquerPoints,
            Experience = profile.Progression.Experience,

            LegacyDeed = 0,
            LegacyMedal = 0,
            LegacyMedalSelect = 0,
            VirtuePoints = 0,
            EncodedPreRebirthLevel = checked((uint)profile.Progression.PreRebirthLevel * PreRebirthLevelScale),

            Strength = profile.Attributes.Strength,
            Agility = profile.Attributes.Agility,
            Vitality = profile.Attributes.Vitality,
            Spirit = profile.Attributes.Spirit,
            UnspentAttributePoints = profile.Attributes.UnspentPoints,
            CurrentLife = profile.Vitals.Life,
            CurrentMana = profile.Vitals.Mana,
            PkPoints = profile.PkPoints,

            Level = profile.Progression.Level,
            CurrentProfession = profile.Progression.Profession,
            FirstProfession = profile.Progression.FirstProfession,
            PreviousProfession = profile.Progression.PreviousProfession,
            LegacyNobility = 0,
            RebirthCount = profile.Progression.RebirthCount,
            LegacyAutoAllot = LegacyAutoAllotCompatibilityValue,

            AuraTierScore = 0,
            CoachPointsHundredths = profile.EnlightenmentPoints,
            CoachExperienceShareCount = 0,
            CoachSessionState = 0,
            FlowerStatusTier = 0,
            TitleId = profile.TitleId,
            BoundConquerPoints = profile.Economy.BoundConquerPoints,

            ActiveSubProfessionId = 0,
            PackedSubProfessionPhases = 0,
            RacePoints = 0,
        };
    }
}
