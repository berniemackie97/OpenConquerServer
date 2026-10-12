using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Tests.Configuration;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.GameServer.Tests.Hosting;

public sealed class GameServerCharacterCreationStartupTests
{
    [Fact]
    public async Task StartAsync_ConfiguredValidSpawn_PublishesWorld()
    {
        IHost host = CreateHost(GameServerCharacterCreationConfigurationTests.LoadWithStartingState());

        try
        {
            GameServerWorldState state = host.Services.GetRequiredService<GameServerWorldState>();

            await host.StartAsync(TestContext.Current.CancellationToken);

            Assert.True(state.GetRequiredWorld().Runtime.IsRunning);

            await host.StopAsync(TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task StartAsync_ConfiguredMissingMap_DoesNotPublishWorld()
    {
        GameServerConfiguration configuration = GameServerCharacterCreationConfigurationTests.LoadWithStartingState(values =>
            values["GameServer:CharacterCreation:StartingMapId"] = "1010");

        IHost host = CreateHost(configuration);

        try
        {
            GameServerWorldState state = host.Services.GetRequiredService<GameServerWorldState>();

            await Assert.ThrowsAsync<InvalidDataException>(() => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task StartAsync_ConfiguredOutOfBoundsSpawn_DoesNotPublishWorld()
    {
        GameServerConfiguration configuration = GameServerCharacterCreationConfigurationTests.LoadWithStartingState(values =>
            values["GameServer:CharacterCreation:StartingX"] = "1");

        IHost host = CreateHost(configuration);

        try
        {
            GameServerWorldState state = host.Services.GetRequiredService<GameServerWorldState>();

            await Assert.ThrowsAsync<InvalidDataException>(() => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }
    }

    private static IHost CreateHost(GameServerConfiguration configuration)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        GameServerHost.ConfigureServices(builder.Services, configuration);
        builder.Services.AddSingleton<IAccountDatabaseReadinessVerifier>(new ReadyDatabase());
        builder.Services.AddSingleton<IGameDatabaseReadinessVerifier>(new ReadyDatabase());

        CanonicalContent content = new();
        builder.Services.AddSingleton<IMapDefinitionCatalogRepository>(content);
        builder.Services.AddSingleton<IMapBaseTerrainCatalogRepository>(content);
        builder.Services.AddSingleton<IMapTerrainCollisionCatalogRepository>(content);

        return builder.Build();
    }

    private sealed class ReadyDatabase : IAccountDatabaseReadinessVerifier, IGameDatabaseReadinessVerifier
    {
        public Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CanonicalContent :
        IMapDefinitionCatalogRepository, IMapBaseTerrainCatalogRepository, IMapTerrainCollisionCatalogRepository
    {
        private readonly WorldMapContent _map = WorldMapTestContent.Create(1002, 1015, 0);

        public ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapDefinitionCatalog([_map.Definition]));
        }

        public ValueTask<MapBaseTerrainCatalog> LoadAsync(MapDefinitionCatalog definitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapBaseTerrainCatalog([_map.BaseTerrain]));
        }

        public ValueTask<MapTerrainCollisionCatalog> LoadAsync(MapDefinitionCatalog definitions,
            MapBaseTerrainCatalog terrains, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapTerrainCollisionCatalog([_map.Collision]));
        }
    }
}
