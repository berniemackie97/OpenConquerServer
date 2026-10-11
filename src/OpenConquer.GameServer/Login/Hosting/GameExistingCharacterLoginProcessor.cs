using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Items;
using OpenConquer.GameServer.Login.Bootstrap.Maps;
using OpenConquer.GameServer.Login.Bootstrap.Reports;
using OpenConquer.GameServer.Login.Bootstrap.Skills;
using OpenConquer.GameServer.Login.Bootstrap.Social;
using OpenConquer.GameServer.Login.Bootstrap.Syndicates;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Sessions;

namespace OpenConquer.GameServer.Login.Hosting;

/// <summary>
/// Advances one authenticated existing-character connection through the client bootstrap sequence.
/// </summary>
internal sealed class GameExistingCharacterLoginProcessor(ExistingCharacterBootstrapProcessor bootstrap, ExistingCharacterEnterMapProcessor enterMap, ExistingCharacterMapStateAppliedProcessor mapStateApplied, ExistingCharacterItemSetProcessor itemSet, ExistingCharacterFriendListProcessor friendList, ExistingCharacterWeaponSkillSetProcessor weaponSkills, ExistingCharacterMagicSetProcessor magic, ExistingCharacterSyndicateAttributesProcessor syndicate, ExistingCharacterSilentInfoReportProcessor silentInfo, ExistingCharacterStatisticsRequestProcessor statistics, WorldSessionAdmissionProcessor admission)
{
    private readonly ExistingCharacterBootstrapProcessor _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    private readonly ExistingCharacterEnterMapProcessor _enterMap = enterMap ?? throw new ArgumentNullException(nameof(enterMap));
    private readonly ExistingCharacterMapStateAppliedProcessor _mapStateApplied = mapStateApplied ?? throw new ArgumentNullException(nameof(mapStateApplied));
    private readonly ExistingCharacterItemSetProcessor _itemSet = itemSet ?? throw new ArgumentNullException(nameof(itemSet));
    private readonly ExistingCharacterFriendListProcessor _friendList = friendList ?? throw new ArgumentNullException(nameof(friendList));
    private readonly ExistingCharacterWeaponSkillSetProcessor _weaponSkills = weaponSkills ?? throw new ArgumentNullException(nameof(weaponSkills));
    private readonly ExistingCharacterMagicSetProcessor _magic = magic ?? throw new ArgumentNullException(nameof(magic));
    private readonly ExistingCharacterSyndicateAttributesProcessor _syndicate = syndicate ?? throw new ArgumentNullException(nameof(syndicate));
    private readonly ExistingCharacterSilentInfoReportProcessor _silentInfo = silentInfo ?? throw new ArgumentNullException(nameof(silentInfo));
    private readonly ExistingCharacterStatisticsRequestProcessor _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    private readonly WorldSessionAdmissionProcessor _admission = admission ?? throw new ArgumentNullException(nameof(admission));

    public async ValueTask<WorldSession> ProcessAsync(CharacterLoginHandoffResult handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        AwaitingEnterMapConnection awaitingEnterMap = await _bootstrap.ProcessAsync(handoff, cancellationToken).ConfigureAwait(false);
        EnteredMapConnection enteredMap = await _enterMap.ProcessAsync(awaitingEnterMap, cancellationToken).ConfigureAwait(false);
        AwaitingItemSetConnection awaitingItemSet = await _mapStateApplied.ProcessAsync(enteredMap, cancellationToken).ConfigureAwait(false);
        AwaitingFriendListConnection awaitingFriendList = await _itemSet.ProcessAsync(awaitingItemSet, cancellationToken).ConfigureAwait(false);
        AwaitingWeaponSkillSetConnection awaitingWeaponSkills = await _friendList.ProcessAsync(awaitingFriendList, cancellationToken).ConfigureAwait(false);
        AwaitingMagicSetConnection awaitingMagic = await _weaponSkills.ProcessAsync(awaitingWeaponSkills, cancellationToken).ConfigureAwait(false);
        AwaitingSyndicateAttributesConnection awaitingSyndicate = await _magic.ProcessAsync(awaitingMagic, cancellationToken).ConfigureAwait(false);
        AwaitingSilentInfoReportConnection awaitingSilentInfo = await _syndicate.ProcessAsync(awaitingSyndicate, cancellationToken).ConfigureAwait(false);
        AwaitingStatisticsRequestConnection awaitingStatistics = await _silentInfo.ProcessAsync(awaitingSilentInfo, cancellationToken).ConfigureAwait(false);
        ExistingCharacterBootstrapCompleteConnection completed = await _statistics.ProcessAsync(awaitingStatistics, cancellationToken).ConfigureAwait(false);

        return await _admission.ProcessAsync(completed, cancellationToken).ConfigureAwait(false);
    }
}
