using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Items;
using OpenConquer.GameServer.Login.Bootstrap.Maps;
using OpenConquer.GameServer.Login.Bootstrap.Reports;
using OpenConquer.GameServer.Login.Bootstrap.Skills;
using OpenConquer.GameServer.Login.Bootstrap.Social;
using OpenConquer.GameServer.Login.Bootstrap.Syndicates;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Hosting;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Time;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Infrastructure.Content.Items;
using OpenConquer.Infrastructure.DependencyInjection;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;
using OpenConquer.Transport.Sockets;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Composes readiness-gated authoritative world execution and optional bounded GameServer ingress.
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

        services.AddGameLoginTicketRedemptionInfrastructure(configuration.AccountConnectionString,
            new GameLoginTicketRedemptionAttemptLimiterOptions(), configuration.ActiveVerificationKeyId, configuration.VerificationKeys);

        services.AddGamePersistence(configuration.GameConnectionString);

        services.AddWorldMapContent(configuration.MapDefinitions, configuration.TerrainDirectory,
            configuration.TerrainLimits, configuration.CollisionDirectory, configuration.CollisionLimits);

        services.AddSingleton(configuration.World);
        services.AddSingleton<GameServerFailureState>();
        services.AddSingleton<GameServerWorldState>();
        services.AddSingleton<GameServerWorldHostedService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GameServerWorldHostedService>());

        if (configuration.Network is { } network)
        {
            ItemTypeCatalogFileOptions items = configuration.ItemTypes
                ?? throw new InvalidOperationException("Network-enabled GameServer configuration requires canonical item-type content.");

            ConfigureNetworkServices(services, network, items);
        }
    }

    private static void ConfigureNetworkServices(IServiceCollection services, GameNetworkOptions network, ItemTypeCatalogFileOptions items)
    {
        services.AddItemTypeCatalogContent(items);

        services.AddSingleton(network);
        services.AddSingleton<GameServerItemCatalogState>();
        services.AddSingleton(provider => provider.GetRequiredService<GameServerItemCatalogState>().GetRequiredCatalog());

        services.AddSingleton(provider => provider.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Maps);
        services.AddSingleton(provider => provider.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Runtime);

        services.AddSingleton<ICharacterLoginResolver, CharacterLoginResolver>();
        services.AddSingleton<ICharacterItemSetResolver, CharacterItemSetResolver>();

        services.AddSingleton<CharacterPresenceDirectory>();
        services.AddSingleton<ICharacterPresenceReader>(provider => provider.GetRequiredService<CharacterPresenceDirectory>());
        services.AddSingleton<ICharacterPresenceRegistrar>(provider => provider.GetRequiredService<CharacterPresenceDirectory>());

        services.AddSingleton<IGameTickSource, SystemGameTickSource>();

        services.AddSingleton<GameTransportHandshakeProcessor>();
        services.AddSingleton<GameConnectionAuthenticator>();
        services.AddSingleton<GameConnectionHandoffProcessor>();
        services.AddSingleton<CharacterLoginHandoffProcessor>();
        services.AddSingleton<ExistingCharacterBootstrapProcessor>();

        services.AddSingleton<ExistingCharacterEnterMapProcessor>();
        services.AddSingleton<ExistingCharacterMapStateAppliedProcessor>();
        services.AddSingleton<ExistingCharacterItemSetProcessor>();
        services.AddSingleton<ExistingCharacterFriendListProcessor>();
        services.AddSingleton<ExistingCharacterWeaponSkillSetProcessor>();
        services.AddSingleton<ExistingCharacterMagicSetProcessor>();
        services.AddSingleton<ExistingCharacterSyndicateAttributesProcessor>();
        services.AddSingleton<ExistingCharacterSilentInfoReportProcessor>();
        services.AddSingleton<ExistingCharacterStatisticsRequestProcessor>();

        services.AddSingleton<WorldSessionAdmissionProcessor>();
        services.AddSingleton<GameExistingCharacterLoginProcessor>();
        services.AddSingleton<GameLoginConnectionProcessor>();

        services.AddSingleton<Func<GameLoginConnectionProcessor>>(provider =>
            () => provider.GetRequiredService<GameLoginConnectionProcessor>());

        services.AddSingleton<Func<ITransportConnectionListener>>(_ =>
            () => new SocketTransportListener(network.EndPoint, network.ListenBacklog));

        services.AddSingleton(_ => new TransportConnectionAdmissionQueue(network.AdmissionCapacity));
        services.AddSingleton<GameLoginMetrics>();
        services.AddHostedService<GameLoginRuntimeHostedService>();
    }
}
