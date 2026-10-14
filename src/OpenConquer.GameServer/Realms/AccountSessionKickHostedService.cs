using System.Data.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.World.Presence;

namespace OpenConquer.GameServer.Realms;

/// <summary>
/// Disconnects accounts that another realm's login has asked this realm to release, and clears this realm's
/// session records when the GameServer starts and stops. TQ's AccountServer sent the same request to a world
/// server over their link (fee message type 4, FEE_KICKOUT); here it is read from the Accounts database.
/// </summary>
internal sealed partial class AccountSessionKickHostedService(
    IAccountSessionStore store,
    ICharacterPresenceRevoker presence,
    TimeProvider timeProvider,
    GameServerFailureState failureState,
    ILogger<AccountSessionKickHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(1);

    private readonly IAccountSessionStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterPresenceRevoker _presence = presence ?? throw new ArgumentNullException(nameof(presence));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly GameServerFailureState _failureState = failureState ?? throw new ArgumentNullException(nameof(failureState));
    private readonly ILogger<AccountSessionKickHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Records left by an earlier run of this realm's GameServer belong to connections that no longer exist.
        await _store.ResetAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _store.ResetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            LogResetFailed(_logger, exception);
        }
    }

    internal async Task ProcessKicksAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountSessionKick> kicks;

        try
        {
            kicks = await _store.TakeKicksAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            LogPollFailed(_logger, exception);
            return;
        }

        foreach (AccountSessionKick kick in kicks)
        {
            if (kick.CharacterId is { } characterId && _presence.Revoke(characterId))
            {
                LogAccountDisconnected(_logger, kick.AccountId, characterId);
                continue;
            }

            // The account has no session here, so its record was left behind by a session that ended without
            // removing it. Removing it now lets the account's next login elsewhere through.
            if (kick.SessionUid is { } sessionUid)
            {
                try
                {
                    await _store.LeaveAsync(kick.AccountId, sessionUid, cancellationToken).ConfigureAwait(false);
                }
                catch (DbException exception)
                {
                    LogPollFailed(_logger, exception);
                }
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using PeriodicTimer timer = new(s_pollInterval, _timeProvider);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessKicksAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _failureState.Record(exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 6311, Level = LogLevel.Information, Message = "Disconnected account {AccountId} (character {CharacterId}) after it logged in to another realm.")]
    private static partial void LogAccountDisconnected(ILogger logger, uint accountId, uint characterId);
    [LoggerMessage(EventId = 6312, Level = LogLevel.Warning, Message = "Account kick requests could not be processed; the next poll will retry.")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
    [LoggerMessage(EventId = 6313, Level = LogLevel.Warning, Message = "This realm's account session records could not be cleared at shutdown; they are cleared at the next start.")]
    private static partial void LogResetFailed(ILogger logger, Exception exception);
}
