using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.AccountServer.Hosting;

internal sealed partial class AccountDatabaseReadinessHostedService(IAccountDatabaseReadinessVerifier verifier, ILogger<AccountDatabaseReadinessHostedService> logger) : IHostedService
{
    private readonly IAccountDatabaseReadinessVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    private readonly ILogger<AccountDatabaseReadinessHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _verifier.VerifyAsync(cancellationToken).ConfigureAwait(false);
        LogReadinessVerified(_logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Account database readiness verified.")]
    private static partial void LogReadinessVerified(ILogger logger);
}
