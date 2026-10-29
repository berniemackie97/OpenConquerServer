using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login.Admission;
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
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.Time;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Infrastructure.Content.Items;
using OpenConquer.Infrastructure.Content.Maps;
using OpenConquer.Infrastructure.DependencyInjection;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;
using OpenConquer.Transport.Sockets;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Wires up the GameServer: the world runtime, started once its readiness checks pass, and the network
/// listener when it is enabled.
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
        services.AddSingleton<IMapTransitionCatalogRepository>(_ => new FileMapTransitionCatalogRepository(
            configuration.TransitionsPath, configuration.MaximumTransitionFileBytes, configuration.MaximumTransitions));

        services.AddSingleton(typeof(RealmId), configuration.RealmId);
        services.AddSingleton(configuration.World);
        services.AddSingleton(new GameServerCharacterCreationReadiness(configuration.CharacterCreationStartingState));
        services.AddSingleton<GameServerFailureState>();
        services.AddSingleton<GameServerWorldState>();
        services.AddSingleton<GameServerWorldHostedService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GameServerWorldHostedService>());

        if (configuration.CharacterCreationStartingState is { } startingState)
        {
            services.AddGameCharacterCreationPersistence(configuration.GameConnectionString);
            services.AddSingleton(startingState);
            services.AddSingleton<CharacterCreationService>();
            services.AddSingleton<GameCharacterCreationLoginProcessor>();
        }

        if (configuration.Network is { } network)
        {
            services.AddGameCharacterLocationWrite(configuration.GameConnectionString);
            // Verify the location grants after world readiness and before opening a listener.
            services.AddHostedService<GameLocationWriterReadinessHostedService>();

            ItemTypeCatalogFileOptions items = configuration.ItemTypes
                ?? throw new InvalidOperationException("Network-enabled GameServer configuration requires canonical item-type content.");
            RealmStatusOptions realmStatus = configuration.RealmStatus
                ?? throw new InvalidOperationException("Network-enabled GameServer configuration requires realm status reporting.");

            ConfigureNetworkServices(services, network, items, realmStatus, configuration.RealmId);
        }
    }

    private static void ConfigureNetworkServices(IServiceCollection services, GameNetworkOptions network, ItemTypeCatalogFileOptions items,
        RealmStatusOptions realmStatus, RealmId realmId)
    {
        services.AddItemTypeCatalogContent(items);
        services.AddGameServerRealmServices(realmId);

        services.AddSingleton(network);
        services.AddSingleton<GameServerItemCatalogState>();
        services.AddSingleton(provider => provider.GetRequiredService<GameServerItemCatalogState>().GetRequiredCatalog());

        services.AddSingleton(provider => provider.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Maps);
        services.AddSingleton(provider => provider.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Runtime);
        services.AddSingleton(provider => provider.GetRequiredService<GameServerWorldState>().GetRequiredTransitions());

        services.AddSingleton<ICharacterLoginResolver, CharacterLoginResolver>();
        services.AddSingleton<ICharacterItemSetResolver, CharacterItemSetResolver>();
        services.AddSingleton<FencedCharacterAdmission>();

        services.AddSingleton<CharacterPresenceDirectory>();
        services.AddSingleton<ICharacterPresenceReader>(provider => provider.GetRequiredService<CharacterPresenceDirectory>());
        services.AddSingleton<ICharacterPresenceRegistrar>(provider => provider.GetRequiredService<CharacterPresenceDirectory>());
        services.AddSingleton<ICharacterPresenceRevoker>(provider => provider.GetRequiredService<CharacterPresenceDirectory>());
        services.AddSingleton<RealmAccountSessions>();

        services.AddSingleton<IGameTickSource, SystemGameTickSource>();

        services.AddSingleton(realmStatus);
        services.AddSingleton<IRealmLoadSource>(provider => new WorldRealmLoadSource(provider.GetRequiredService<WorldRuntime>()));
        services.AddSingleton<Func<IRealmLoadSource>>(provider => () => provider.GetRequiredService<IRealmLoadSource>());
        services.AddSingleton<GameLoginCapacityGate>();

        services.AddSingleton<GameTransportHandshakeProcessor>();
        services.AddSingleton<GameConnectionAuthenticator>();
        services.AddSingleton<GameConnectionHandoffProcessor>();
        services.AddSingleton<CharacterLoginHandoffProcessor>();
        services.AddSingleton<ExistingCharacterBootstrapProcessor>(provider => new ExistingCharacterBootstrapProcessor(
            provider.GetRequiredService<ICharacterPresenceRegistrar>(),
            provider.GetRequiredService<RealmAccountSessions>(),
            provider.GetRequiredService<WorldMapContentCatalog>(),
            provider.GetRequiredService<GameLoginMetrics>(),
            provider.GetRequiredService<FencedCharacterAdmission>()));

        services.AddSingleton<ExistingCharacterEnterMapProcessor>();
        services.AddSingleton<ExistingCharacterMapStateAppliedProcessor>();
        services.AddSingleton<ExistingCharacterItemSetProcessor>();
        services.AddSingleton<ExistingCharacterFriendListProcessor>();
        services.AddSingleton<ExistingCharacterWeaponSkillSetProcessor>();
        services.AddSingleton<ExistingCharacterMagicSetProcessor>();
        services.AddSingleton<ExistingCharacterSyndicateAttributesProcessor>();
        services.AddSingleton<ExistingCharacterSilentInfoReportProcessor>();
        services.AddSingleton<ExistingCharacterStatisticsRequestProcessor>();

        services.AddSingleton<WorldVisibilityService>();
        services.AddSingleton<WorldConnectedPortalTransferCoordinator>();
        services.AddSingleton<WorldSessionAdmissionProcessor>(provider => new WorldSessionAdmissionProcessor(
            provider.GetRequiredService<WorldRuntime>(),
            provider.GetRequiredService<WorldVisibilityService>(),
            provider.GetRequiredService<FencedCharacterAdmission>(),
            provider.GetRequiredService<WorldConnectedPortalTransferCoordinator>(),
            provider.GetRequiredService<GameServerFailureState>()));
        services.AddSingleton<GameExistingCharacterLoginProcessor>();
        services.AddSingleton<GameLoginConnectionProcessor>();

        services.AddSingleton<Func<GameLoginConnectionProcessor>>(provider =>
            () => provider.GetRequiredService<GameLoginConnectionProcessor>());

        services.AddSingleton<Func<ITransportConnectionListener>>(_ =>
            () => new SocketTransportListener(network.EndPoint, network.ListenBacklog));

        services.AddSingleton(_ => new TransportConnectionAdmissionQueue(network.AdmissionCapacity));
        services.AddSingleton<GameLoginMetrics>();

        // Clears this realm's account session records before ingress opens and after it closes.
        services.AddHostedService<AccountSessionKickHostedService>();
        services.AddHostedService<GameLoginRuntimeHostedService>();

        // Registered after ingress so that the realm is reported available only once its listener is open,
        // and withdrawn before ingress stops.
        services.AddHostedService<RealmStatusHostedService>();
    }
}
