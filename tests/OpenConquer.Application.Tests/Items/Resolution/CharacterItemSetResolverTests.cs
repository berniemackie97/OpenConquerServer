using OpenConquer.Application.Items.Catalog;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Resolution;

public sealed class CharacterItemSetResolverTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint ItemTypeId = 100_000;
    private static readonly DateTimeOffset s_utcNow = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        FakeRepository repository = new(new CharacterItemSet(CharacterId, []));
        ItemTypeCatalog itemTypes = CreateCatalog();

        Assert.Throws<ArgumentNullException>(() => new CharacterItemSetResolver(null!, itemTypes));
        Assert.Throws<ArgumentNullException>(() => new CharacterItemSetResolver(repository, null!));
    }

    [Fact]
    public async Task ResolveAsync_ValidSetWithoutExpiredItems_ReturnsRepositoryInstance()
    {
        CharacterItemSet persisted = new(CharacterId, [CreateItem(10), CreateItem(20)]);
        FakeRepository repository = new(persisted);
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());

        CharacterItemSet result = await resolver.ResolveAsync(CharacterId, s_utcNow, TestContext.Current.CancellationToken);

        Assert.Same(persisted, result);
        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(CharacterId, repository.CharacterId);
        Assert.Equal(s_utcNow, repository.UtcNow);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredItems_AreRemovedAndOrderingRemainsDeterministic()
    {
        CharacterItemSet persisted = new(CharacterId,
        [
            CreateItem(30), CreateItem(10),
            CreateItem(20, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow)),
            CreateItem(40, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(-1))),
        ]);
        CharacterItemSetResolver resolver = new(new FakeRepository(persisted), CreateCatalog());

        CharacterItemSet result = await resolver.ResolveAsync(CharacterId, s_utcNow, TestContext.Current.CancellationToken);

        Assert.NotSame(persisted, result);
        Assert.Equal([10u, 30u], result.Items.Select(static item => item.ItemId));
    }

    [Fact]
    public async Task ResolveAsync_ExpiredUnknownItemType_IsRemovedBeforeCatalogLookup()
    {
        CharacterItem item = CreateItem(1, itemTypeId: 999_999, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow));
        CharacterItemSetResolver resolver = new(new FakeRepository(new CharacterItemSet(CharacterId, [item])), CreateCatalog());

        CharacterItemSet result = await resolver.ResolveAsync(CharacterId, s_utcNow, TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ResolveAsync_UnknownActiveItemType_FailsClosed()
    {
        CharacterItem item = CreateItem(1, itemTypeId: 999_999);
        CharacterItemSetResolver resolver = new(new FakeRepository(new CharacterItemSet(CharacterId, [item])), CreateCatalog());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(CharacterId, s_utcNow, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("unknown item type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("999999", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_RepositoryReturnsDifferentCharacter_FailsClosed()
    {
        uint otherCharacterId = CharacterId + 1;
        CharacterItemSetResolver resolver = new(new FakeRepository(new CharacterItemSet(otherCharacterId, [])), CreateCatalog());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => resolver.ResolveAsync(CharacterId, s_utcNow, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(otherCharacterId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(CharacterId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task ResolveAsync_NonPlayerCharacterId_IsRejectedBeforeRepositoryLoad(uint characterId)
    {
        FakeRepository repository = new(new CharacterItemSet(CharacterId, []));
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => resolver.ResolveAsync(characterId, s_utcNow, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("characterId", exception.ParamName);
        Assert.Equal(0, repository.LoadCount);
    }

    [Fact]
    public async Task ResolveAsync_NonUtcTimestamp_IsRejectedBeforeRepositoryLoad()
    {
        FakeRepository repository = new(new CharacterItemSet(CharacterId, []));
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveAsync(CharacterId, s_utcNow.ToOffset(TimeSpan.FromHours(-4)), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("utcNow", exception.ParamName);
        Assert.Equal(0, repository.LoadCount);
    }

    [Fact]
    public async Task ResolveAsync_PreCanceledOperation_DoesNotLoad()
    {
        FakeRepository repository = new(new CharacterItemSet(CharacterId, []));
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(CharacterId, s_utcNow, cancellation.Token).AsTask());

        Assert.Equal(0, repository.LoadCount);
    }

    [Fact]
    public async Task ResolveAsync_CancellationImmediatelyAfterRepositoryReturn_IsObserved()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancelingRepository repository = new(new CharacterItemSet(CharacterId, []), cancellation);
        CharacterItemSetResolver resolver = new(repository, CreateCatalog());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(CharacterId, s_utcNow, cancellation.Token).AsTask());

        Assert.Equal(1, repository.LoadCount);
    }

    private static CharacterItem CreateItem(uint itemId, uint itemTypeId = ItemTypeId, ItemLifetime? lifetime = null)
    {
        return new CharacterItem(itemId, CharacterId, itemTypeId, ItemPlacement.CreateInventory(), 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, 0, null, 0, 0, 0, 1, lifetime ?? ItemLifetime.CreatePermanent());
    }

    private static ItemTypeCatalog CreateCatalog()
    {
        return new ItemTypeCatalog([new ItemTypeDefinition(ItemTypeId, $"Item{ItemTypeId}", 0, 0, 0, 0, 0, 0, 0, 1)]);
    }

    private sealed class FakeRepository(CharacterItemSet result) : ICharacterItemSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public uint? CharacterId { get; private set; }
        public DateTimeOffset? UtcNow { get; private set; }

        public ValueTask<CharacterItemSet> LoadAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            CharacterId = characterId;
            UtcNow = utcNow;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelingRepository(CharacterItemSet result, CancellationTokenSource cancellation) : ICharacterItemSetRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public ValueTask<CharacterItemSet> LoadAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
