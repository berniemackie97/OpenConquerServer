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

    /// <summary>
    /// Returns <see langword="null"/> when the login was refused because its account is still playing in another realm.
    /// </summary>
    public async ValueTask<WorldSession?> ProcessAsync(CharacterLoginHandoffResult handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        ExistingCharacterLoginContext? login = await _bootstrap.ProcessAsync(handoff, cancellationToken).ConfigureAwait(false);

        if (login is null)
        {
            return null;
        }

        ExistingCharacterBootstrapCompleteConnection completed;

        try
        {
            using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, login.Connection.RevocationToken);
            CancellationToken operationToken = operationCancellation.Token;

            await _enterMap.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _mapStateApplied.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _itemSet.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _friendList.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _weaponSkills.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _magic.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _syndicate.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _silentInfo.ProcessAsync(login, operationToken).ConfigureAwait(false);
            await _statistics.ProcessAsync(login, operationToken).ConfigureAwait(false);

            completed = login.Complete();
        }
        catch (Exception loginFailure)
        {
            try
            {
                await login.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw CreateLoginFailure(loginFailure, cleanupFailure);
            }

            throw;
        }

        return await _admission.ProcessAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    private static AggregateException CreateLoginFailure(Exception loginFailure, Exception cleanupFailure)
    {
        List<Exception> failures = [loginFailure];

        if (cleanupFailure is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupFailure);
        }

        return new AggregateException("Existing-character login failed and connection cleanup also failed.", failures);
    }
}
