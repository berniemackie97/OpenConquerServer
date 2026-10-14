using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Application.Realms;
using OpenConquer.Application.World;
using OpenConquer.Domain.Items;
using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Hosting;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.Tests.Configuration;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Tests.Login.Hosting;

public sealed class GameLoginRuntimeHostedServiceTests
{
    [Fact]
    public async Task Host_ItemCatalogFailurePreventsNetworkListenerCreation()
    {
        IOException failure = new("canonical item content is unavailable");
        ItemRepository items = new(failure);
        TestListener listener = new();
        int listenerCalls = 0;

        IHost host = CreateHost(items, listener, new RecordingAccountSessionStore(), () => Interlocked.Increment(ref listenerCalls));

        try
        {
            IOException observed = await Assert.ThrowsAsync<IOException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Same(failure, observed);
            Assert.Equal(1, items.Calls);
            Assert.Equal(0, listenerCalls);
            Assert.Equal(0, listener.DisposeCount);
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_StartsListenerOnlyAfterWorldAndItemContentReadiness()
    {
        ItemRepository items = new();
        TestListener listener = new();
        RecordingAccountSessionStore accountSessions = new();
        int listenerCalls = 0;
        IHost? host = null;

        host = CreateHost(items, listener, accountSessions, () =>
        {
            Assert.Equal(1, items.Calls);
            Assert.True(host!.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Runtime.IsRunning);
            Interlocked.Increment(ref listenerCalls);
        });

        try
        {
            IHostedService[] services = host.Services.GetServices<IHostedService>().ToArray();

            Assert.Collection(services,
                service => Assert.IsType<GameServerWorldHostedService>(service),
                service => Assert.IsType<AccountSessionKickHostedService>(service),
                service => Assert.IsType<GameLoginRuntimeHostedService>(service),
                service => Assert.IsType<RealmStatusHostedService>(service));

            await host.StartAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, listenerCalls);
            Assert.Equal(1, items.Calls);
            Assert.Equal(1, accountSessions.ResetCount);
            Assert.True(host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld().Runtime.IsRunning);

            ItemTypeCatalog published = host.Services.GetRequiredService<GameServerItemCatalogState>().GetRequiredCatalog();

            Assert.Equal(1, published.Count);

            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.DisposeCount);
            Assert.Equal(2, accountSessions.ResetCount);
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld());
            Assert.Null(host.Services.GetRequiredService<GameServerFailureState>().Failure);
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_PeerDisconnectDuringHandshakeDisposesConnectionWithoutFatalShutdown()
    {
        ItemRepository items = new();
        TestListener listener = new();
        IHost host = CreateHost(items, listener, new RecordingAccountSessionStore(), static () => { });

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);

            FakeGameTransportConnection connection = new();
            connection.QueueEndOfStream();

            listener.Queue(connection);

            await connection.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.Equal(1, connection.DisposeCount);
            Assert.Null(host.Services.GetRequiredService<GameServerFailureState>().Failure);

            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.DisposeCount);
            Assert.Null(host.Services.GetRequiredService<GameServerFailureState>().Failure);
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    private static IHost CreateHost(ItemRepository items, TestListener listener, IAccountSessionStore accountSessions, Action onListenerCreated)
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(values =>
            {
                values["GameServer:Network:Enabled"] = "true";
                values["GameServer:Network:BindAddress"] = "127.0.0.1";
                values["GameServer:Network:Port"] = "5816";
                values["GameServer:Network:ListenBacklog"] = "64";
                values["GameServer:Network:AdmissionCapacity"] = "8";
                values["GameServer:Network:MaximumConnectionsPerSource"] = "2";
                values["GameServer:Network:WorkerCount"] = "2";
                values["GameServer:Network:ConnectionTimeout"] = "00:00:05";
                values["GameServer:RealmStatus:ReportInterval"] = "00:00:10";

                values["GameServer:Items:FilePath"] = "content/items/item-types.json";
                values["GameServer:Items:MaximumFileBytes"] = "16777216";
                values["GameServer:Items:MaximumDefinitions"] = "200000";
            }), Path.GetTempPath());

        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        GameServerHost.ConfigureServices(builder.Services, configuration);

        builder.Services.AddSingleton<IAccountDatabaseReadinessVerifier, ReadyDatabase>();
        builder.Services.AddSingleton<IGameDatabaseReadinessVerifier, ReadyDatabase>();
        builder.Services.AddSingleton<IGameRealmBindingVerifier, ReadyDatabase>();
        builder.Services.AddSingleton<IRealmStatusPublisher, NullRealmStatusPublisher>();
        builder.Services.AddSingleton(accountSessions);

        builder.Services.AddSingleton<MapRepositories>();
        builder.Services.AddSingleton<IMapDefinitionCatalogRepository>(provider => provider.GetRequiredService<MapRepositories>());
        builder.Services.AddSingleton<IMapBaseTerrainCatalogRepository>(provider => provider.GetRequiredService<MapRepositories>());
        builder.Services.AddSingleton<IMapTerrainCollisionCatalogRepository>(provider => provider.GetRequiredService<MapRepositories>());

        builder.Services.AddSingleton<IItemTypeCatalogRepository>(items);
        builder.Services.AddSingleton<Func<ITransportConnectionListener>>(_ => () =>
        {
            onListenerCreated();
            return listener;
        });

        return builder.Build();
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }
    }

    private sealed class NullRealmStatusPublisher : IRealmStatusPublisher
    {
        public ValueTask PublishAsync(RealmLoad load, int onlineCount, TimeSpan lifetime, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask WithdrawAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ReadyDatabase : IAccountDatabaseReadinessVerifier, IGameDatabaseReadinessVerifier, IGameRealmBindingVerifier
    {
        public Task VerifyAsync(RealmId expectedRealmId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(1u, expectedRealmId.Value);
            return Task.CompletedTask;
        }

        public Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class MapRepositories :
        IMapDefinitionCatalogRepository, IMapBaseTerrainCatalogRepository, IMapTerrainCollisionCatalogRepository
    {
        private readonly WorldMapContent _map = WorldMapTestContent.Create(1002, 1015, 0);

        public ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapDefinitionCatalog([_map.Definition]));
        }

        public ValueTask<MapBaseTerrainCatalog> LoadAsync(
            MapDefinitionCatalog definitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapBaseTerrainCatalog([_map.BaseTerrain]));
        }

        public ValueTask<MapTerrainCollisionCatalog> LoadAsync(
            MapDefinitionCatalog definitions, MapBaseTerrainCatalog terrains,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapTerrainCollisionCatalog([_map.Collision]));
        }
    }

    private sealed class ItemRepository(Exception? failure = null) : IItemTypeCatalogRepository
    {
        private readonly ItemTypeCatalog _catalog = new([new ItemTypeDefinition(
            100001, "Test Item", requiredLevel: 1, speedPercentOffset: 0, life: 0, mana: 0,
            initialDurability: 1, maximumDurability: 1, staticLifetimeMinutes: 0, stackCapacity: 1)]);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<ItemTypeCatalog> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);

            if (failure is not null)
            {
                return ValueTask.FromException<ItemTypeCatalog>(failure);
            }

            return ValueTask.FromResult(_catalog);
        }
    }

    private sealed class TestListener : ITransportConnectionListener
    {
        private readonly Channel<ITransportConnection> _connections = Channel.CreateUnbounded<ITransportConnection>();
        private int _disposals;

        public int DisposeCount => Volatile.Read(ref _disposals);

        public void Queue(ITransportConnection connection)
        {
            if (!_connections.Writer.TryWrite(connection))
            {
                throw new InvalidOperationException("Test listener is not accepting connections.");
            }
        }

        public ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            return _connections.Reader.ReadAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposals);
            _connections.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
