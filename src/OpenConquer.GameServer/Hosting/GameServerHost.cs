using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.GameServer.Configuration;
using OpenConquer.Infrastructure.Accounts.Extensions;
using OpenConquer.Infrastructure.Content.Maps;
using OpenConquer.Infrastructure.Persistence.Game.Extensions;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Composes the readiness-gated GameServer world process without exposing a network listener or gameplay dispatcher.
/// </summary>
internal static class GameServerHost
{
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromMinutes(1);

    public static IHost Create(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        GameServerConfiguration configuration = GameServerConfiguration.Load(builder.Configuration, builder.Environment.ContentRootPath);

        ConfigureServices(builder.Services, configuration);

        return builder.Build();
    }

    internal static void ConfigureServices(IServiceCollection services, GameServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<HostOptions>(options =>
        {
            options.StartupTimeout = s_startupTimeout;
            options.ShutdownTimeout = s_shutdownTimeout;
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        services.AddGameLoginTicketRedemptionInfrastructure(configuration.AccountConnectionString, new GameLoginTicketRedemptionAttemptLimiterOptions(), configuration.ActiveVerificationKeyId, configuration.VerificationKeys);

        services.AddGamePersistence(configuration.GameConnectionString);

        services.AddWorldMapContent(configuration.MapDefinitions, configuration.TerrainDirectory, configuration.TerrainLimits, configuration.CollisionDirectory, configuration.CollisionLimits);

        services.AddSingleton(configuration.World);
        services.AddSingleton<FatalBackgroundServiceFailureState>();
        services.AddSingleton<GameServerWorldState>();
        services.AddSingleton<GameServerWorldHostedService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GameServerWorldHostedService>());
    }
}
