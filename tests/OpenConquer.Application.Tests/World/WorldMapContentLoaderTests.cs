using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class WorldMapContentLoaderTests
{
    [Fact]
    public async Task LoadAsync_ValidCatalogs_ResolvesWorldMapsAndSharesTerrain()
    {
        Fixture fixture = new();

        WorldMapContentCatalog catalog = await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, catalog.Count);
        Assert.Equal(new[] { "definitions", "terrains", "collisions" }, fixture.Calls);

        Assert.True(catalog.TryGet(1000, out WorldMapContent? first));
        Assert.True(catalog.TryGet(1001, out WorldMapContent? second));
        Assert.True(catalog.TryGet(1002, out WorldMapContent? third));

        Assert.Equal(500u, first.MapDataId);
        Assert.Equal(501u, third.MapDataId);
        Assert.Equal(ulong.MaxValue, first.Flags);
        Assert.Equal(0ul, second.Flags);
        Assert.Same(first.Collision, second.Collision);
        Assert.Same(first.BaseTerrain, second.BaseTerrain);
        Assert.Same(fixture.FirstCollision, first.Collision);
        Assert.Same(fixture.SecondCollision, third.Collision);
        Assert.False(catalog.TryGet(500, out _));
    }

    [Fact]
    public async Task LoadAsync_MissingBaseTerrain_IsRejected()
    {
        Fixture fixture = new();
        fixture.TerrainRepository.Catalog = new MapBaseTerrainCatalog([fixture.FirstTerrain]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("requires 2 terrain identities", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnexpectedBaseTerrain_IsRejected()
    {
        Fixture fixture = new();
        MapBaseTerrain extra = CreateTerrain(502);

        fixture.TerrainRepository.Catalog = new MapBaseTerrainCatalog([fixture.FirstTerrain, fixture.SecondTerrain, extra]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_MissingCollision_IsRejected()
    {
        Fixture fixture = new();
        fixture.CollisionRepository.Catalog = new MapTerrainCollisionCatalog([fixture.FirstCollision]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_UnexpectedCollision_IsRejected()
    {
        Fixture fixture = new();
        MapTerrainCollision extra = new(CreateTerrain(502), []);

        fixture.CollisionRepository.Catalog = new MapTerrainCollisionCatalog([fixture.FirstCollision, fixture.SecondCollision, extra]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_DifferentBaseTerrainSnapshot_IsRejected()
    {
        Fixture fixture = new();
        MapTerrainCollision incompatible = new(CreateTerrain(500), []);

        fixture.CollisionRepository.Catalog = new MapTerrainCollisionCatalog([incompatible, fixture.SecondCollision]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("different base-terrain snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_DoesNotAccessRepositories()
    {
        Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.CreateLoader().LoadAsync(cancellation.Token));

        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task LoadAsync_DefinitionFailure_DoesNotLoadDependentCatalogs()
    {
        Fixture fixture = new();
        fixture.DefinitionRepository.Failure = new InvalidDataException("Invalid map definitions.");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "definitions" }, fixture.Calls);
    }

    [Fact]
    public async Task LoadAsync_TerrainFailure_DoesNotLoadCollisionCatalog()
    {
        Fixture fixture = new();
        fixture.TerrainRepository.Failure = new InvalidDataException("Invalid terrain.");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "definitions", "terrains" }, fixture.Calls);
    }

    [Fact]
    public async Task LoadAsync_CollisionFailure_DoesNotPublishSnapshot()
    {
        Fixture fixture = new();
        fixture.CollisionRepository.Failure = new InvalidDataException("Invalid collision.");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateLoader().LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "definitions", "terrains", "collisions" }, fixture.Calls);
    }

    [Fact]
    public void Constructor_NullRepositories_AreRejected()
    {
        Fixture fixture = new();

        Assert.Throws<ArgumentNullException>(() => new WorldMapContentLoader(null!, fixture.TerrainRepository, fixture.CollisionRepository));
        Assert.Throws<ArgumentNullException>(() => new WorldMapContentLoader(fixture.DefinitionRepository, null!, fixture.CollisionRepository));
        Assert.Throws<ArgumentNullException>(() => new WorldMapContentLoader(fixture.DefinitionRepository, fixture.TerrainRepository, null!));
    }

    private static MapBaseTerrain CreateTerrain(uint mapDataId)
    {
        return new MapBaseTerrain(mapDataId, 1, 1, [new MapBaseTerrainCell(10, 0, -5)], []);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            FirstTerrain = CreateTerrain(500);
            SecondTerrain = CreateTerrain(501);
            FirstCollision = new MapTerrainCollision(FirstTerrain, []);
            SecondCollision = new MapTerrainCollision(SecondTerrain, []);

            MapDefinitionCatalog definitions = new([
                new MapDefinition(1000, 500, ulong.MaxValue),
                new MapDefinition(1001, 500, 0),
                new MapDefinition(1002, 501, 1)
            ]);

            DefinitionRepository = new StubDefinitionRepository(definitions, Calls);
            TerrainRepository = new StubTerrainRepository(new MapBaseTerrainCatalog([FirstTerrain, SecondTerrain]), Calls);
            CollisionRepository = new StubCollisionRepository(new MapTerrainCollisionCatalog([FirstCollision, SecondCollision]), Calls);
        }

        public List<string> Calls { get; } = [];
        public MapBaseTerrain FirstTerrain { get; }
        public MapBaseTerrain SecondTerrain { get; }
        public MapTerrainCollision FirstCollision { get; }
        public MapTerrainCollision SecondCollision { get; }
        public StubDefinitionRepository DefinitionRepository { get; }
        public StubTerrainRepository TerrainRepository { get; }
        public StubCollisionRepository CollisionRepository { get; }

        public WorldMapContentLoader CreateLoader() => new(DefinitionRepository, TerrainRepository, CollisionRepository);
    }

    private sealed class StubDefinitionRepository(MapDefinitionCatalog catalog, List<string> calls) : IMapDefinitionCatalogRepository
    {
        public Exception? Failure { get; set; }

        public ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("definitions");

            if (Failure is not null)
            {
                throw Failure;
            }

            return ValueTask.FromResult(catalog);
        }
    }

    private sealed class StubTerrainRepository(MapBaseTerrainCatalog catalog, List<string> calls) : IMapBaseTerrainCatalogRepository
    {
        public MapBaseTerrainCatalog Catalog { get; set; } = catalog;
        public Exception? Failure { get; set; }

        public ValueTask<MapBaseTerrainCatalog> LoadAsync(MapDefinitionCatalog definitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("terrains");

            if (Failure is not null)
            {
                throw Failure;
            }

            return ValueTask.FromResult(Catalog);
        }
    }

    private sealed class StubCollisionRepository(MapTerrainCollisionCatalog catalog, List<string> calls) : IMapTerrainCollisionCatalogRepository
    {
        public MapTerrainCollisionCatalog Catalog { get; set; } = catalog;
        public Exception? Failure { get; set; }

        public ValueTask<MapTerrainCollisionCatalog> LoadAsync(MapDefinitionCatalog definitions, MapBaseTerrainCatalog terrains,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("collisions");

            if (Failure is not null)
            {
                throw Failure;
            }

            return ValueTask.FromResult(Catalog);
        }
    }
}
