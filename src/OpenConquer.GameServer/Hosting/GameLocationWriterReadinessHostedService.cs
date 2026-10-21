using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Characters.Location;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Prevents the login listener from advertising an available realm when its dedicated
/// character-location writer cannot read and update the authorized v13 columns.
/// </summary>
internal sealed partial class GameLocationWriterReadinessHostedService(
    ICharacterLocationWriterReadinessVerifier writer, ILogger<GameLocationWriterReadinessHostedService> logger) : IHostedService
{
    private readonly ICharacterLocationWriterReadinessVerifier _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly ILogger<GameLocationWriterReadinessHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _writer.VerifyPermissionsAsync(cancellationToken).ConfigureAwait(false);
        LogWriterReady(_logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 6007, Level = LogLevel.Information,
        Message = "GameServer dedicated character-location writer readiness verified.")]
    private static partial void LogWriterReady(ILogger logger);
}
