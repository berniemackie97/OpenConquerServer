using System.Data.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Realms;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;

namespace OpenConquer.GameServer.Realms;

/// <summary>
/// Reports this GameServer's realm load to the AccountServer while it accepts logins, and withdraws the
/// report on shutdown. TQ's world server sent the same state over its AccountServer link every 30 seconds.
/// </summary>
/// <remarks>
/// A report that cannot be written is retried on the next interval. If writes keep failing the last report
/// expires and the AccountServer refuses logins to the realm, which is the correct failure mode.
/// </remarks>
internal sealed partial class RealmStatusHostedService(
    IRealmStatusPublisher publisher,
    Func<IRealmLoadSource> loadSourceFactory,
    RealmStatusOptions options,
    TimeProvider timeProvider,
    GameServerFailureState failureState,
    ILogger<RealmStatusHostedService> logger) : BackgroundService
{
    private readonly IRealmStatusPublisher _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    private readonly Func<IRealmLoadSource> _loadSourceFactory = loadSourceFactory ?? throw new ArgumentNullException(nameof(loadSourceFactory));
    private readonly RealmStatusOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly GameServerFailureState _failureState = failureState ?? throw new ArgumentNullException(nameof(failureState));
    private readonly ILogger<RealmStatusHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _publisher.WithdrawAsync(cancellationToken).ConfigureAwait(false);
            LogStatusWithdrawn(_logger);
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            LogWithdrawFailed(_logger, _options.ReportLifetime, exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            IRealmLoadSource loadSource = _loadSourceFactory();
            using PeriodicTimer timer = new(_options.ReportInterval, _timeProvider);

            do
            {
                await PublishAsync(loadSource, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
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

    private async Task PublishAsync(IRealmLoadSource loadSource, CancellationToken cancellationToken)
    {
        RealmLoadSample sample = loadSource.Sample();

        try
        {
            await _publisher.PublishAsync(sample.Load, sample.OnlineCount, _options.ReportLifetime, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            LogPublishFailed(_logger, exception);
        }
    }

    [LoggerMessage(EventId = 6300, Level = LogLevel.Information, Message = "Realm status report withdrawn.")]
    private static partial void LogStatusWithdrawn(ILogger logger);
    [LoggerMessage(EventId = 6301, Level = LogLevel.Warning, Message = "Realm status report could not be written; the next interval will retry.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception);
    [LoggerMessage(EventId = 6302, Level = LogLevel.Warning, Message = "Realm status report could not be withdrawn; it expires within {ReportLifetime}.")]
    private static partial void LogWithdrawFailed(ILogger logger, TimeSpan reportLifetime, Exception exception);
}
