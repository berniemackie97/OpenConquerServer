using OpenConquer.Application.Characters.Creation;

namespace OpenConquer.Application.Tests.Characters.Creation;

public sealed class CharacterCreationServiceTests
{
    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        CapturingStore store = new();
        CharacterCreationStartingState state = CreateState();

        Assert.Throws<ArgumentNullException>(() => new CharacterCreationService(null!, state));
        Assert.Throws<ArgumentNullException>(() => new CharacterCreationService(store, null!));
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_UsesTrustedAccountIdentityAndStartingState()
    {
        CapturingStore store = new();
        CharacterCreationService service = new(store, CreateState());
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        CharacterCreationResult result = await service.CreateAsync(
            42, "NewHero", 2001, 100, cancellationToken);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal(1_000_000u, result.CharacterId);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(cancellationToken, store.LastCancellationToken);

        CharacterCreationCandidate character = Assert.IsType<CharacterCreationCandidate>(store.Captured);

        Assert.Equal(42u, character.AccountId);
        Assert.Equal("NewHero", character.Name);
        Assert.Equal((ushort)2001, character.Body);
        Assert.Equal((byte)100, character.Profession);
        Assert.Equal(12_001u, character.AppearanceComposite);

        Assert.Equal((ushort)0, character.Strength);
        Assert.Equal((ushort)2, character.Agility);
        Assert.Equal((ushort)3, character.Vitality);
        Assert.Equal((ushort)5, character.Spirit);

        Assert.Equal((ushort)93, character.CurrentLife);
        Assert.Equal((ushort)0, character.CurrentMana);

        Assert.Equal(1010u, character.MapId);
        Assert.Equal((ushort)61, character.PositionX);
        Assert.Equal((ushort)101, character.PositionY);
    }

    [Fact]
    public async Task CreateAsync_RejectsZeroAccountWithoutCallingPersistence()
    {
        CapturingStore store = new();
        CharacterCreationService service = new(store, CreateState());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.CreateAsync(0, "NewHero", 1003, 10, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("Bad Name")]
    [InlineData("Bad;Name")]
    [InlineData("NotEncodable\u4E2D")]
    public async Task CreateAsync_InvalidName_DoesNotCallPersistence(string? name)
    {
        CapturingStore store = new();
        CharacterCreationService service = new(store, CreateState());

        CharacterCreationResult result = await service.CreateAsync(42, name, 1003, 10, TestContext.Current.CancellationToken);

        Assert.Equal(CharacterCreationStatus.InvalidName, result.Status);
        Assert.Equal(0u, result.CharacterId);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(1001, 10)]
    [InlineData(2003, 10)]
    [InlineData(1003, 11)]
    [InlineData(1003, 30)]
    [InlineData(1003, 101)]
    public async Task CreateAsync_InvalidSelection_DoesNotCallPersistence(
        ushort body,
        ushort profession)
    {
        CapturingStore store = new();
        CharacterCreationService service = new(store, CreateState());

        CharacterCreationResult result = await service.CreateAsync(42, "NewHero", body, profession, TestContext.Current.CancellationToken);

        Assert.Equal(CharacterCreationStatus.InvalidSelection, result.Status);
        Assert.Equal(0u, result.CharacterId);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(CharacterCreationStoreStatus.AccountAlreadyHasCharacter, CharacterCreationStatus.AccountAlreadyHasCharacter)]
    [InlineData(CharacterCreationStoreStatus.NameAlreadyInUse, CharacterCreationStatus.NameAlreadyInUse)]
    public async Task CreateAsync_PersistenceConflict_PreservesBusinessStatus(
        CharacterCreationStoreStatus storeStatus,
        CharacterCreationStatus expectedStatus)
    {
        CapturingStore store = new()
        {
            Result = storeStatus switch
            {
                CharacterCreationStoreStatus.AccountAlreadyHasCharacter => CharacterCreationStoreResult.AccountAlreadyHasCharacter(),
                CharacterCreationStoreStatus.NameAlreadyInUse => CharacterCreationStoreResult.NameAlreadyInUse(),
                _ => throw new ArgumentOutOfRangeException(nameof(storeStatus)),
            },
        };

        CharacterCreationService service = new(store, CreateState());

        CharacterCreationResult result = await service.CreateAsync(42, "NewHero", 1003, 10, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(0u, result.CharacterId);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task CreateAsync_UnexpectedPersistenceStatus_FailsClosed()
    {
        CapturingStore store = new()
        {
            Result = default,
        };

        CharacterCreationService service = new(store, CreateState());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateAsync(42, "NewHero", 1003, 10, TestContext.Current.CancellationToken));

        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task CreateAsync_PersistenceFailure_IsNotConvertedOrRetried()
    {
        InvalidOperationException failure = new("Commit outcome is uncertain.");

        CapturingStore store = new()
        {
            Failure = failure,
        };

        CharacterCreationService service = new(store, CreateState());

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateAsync(42, "NewHero", 1003, 10, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task CreateAsync_PreCanceledOperation_DoesNotCallPersistence()
    {
        CapturingStore store = new();
        CharacterCreationService service = new(store, CreateState());

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.CreateAsync(42, "NewHero", 1003, 10, cancellation.Token));

        Assert.Equal(0, store.CallCount);
    }

    private static CharacterCreationStartingState CreateState()
    {
        CharacterCreationStartingAllotment[] allotments =
        [
            new(1, 5, 2, 3, 0),
            new(2, 5, 2, 3, 0),
            new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0),
            new(6, 3, 1, 4, 2),
            new(10, 0, 2, 3, 5),
        ];

        return new CharacterCreationStartingState(1010, 61, 101, allotments);
    }

    private sealed class CapturingStore : ICharacterCreationStore
    {
        public CharacterCreationStoreResult Result { get; set; } = CharacterCreationStoreResult.Created(1_000_000);
        public Exception? Failure { get; set; }

        public int CallCount { get; private set; }
        public CharacterCreationCandidate? Captured { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<CharacterCreationStoreResult> CreateAsync(
            CharacterCreationCandidate character,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            Captured = character;
            LastCancellationToken = cancellationToken;

            if (Failure is not null)
            {
                return ValueTask.FromException<CharacterCreationStoreResult>(Failure);
            }

            return ValueTask.FromResult(Result);
        }
    }
}
