using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;
using OpenConquer.Infrastructure.DependencyInjection;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game.Characters;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterCreationStoreTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 1_500_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_PersistsCompleteStartingCharacter()
    {
        uint accountId = CreateAccountId();
        CharacterCreationCandidate character = CreateCandidate(accountId, CreateCharacterName());

        ICharacterCreationStore store = CreateStore();

        CharacterCreationStoreResult result = await store.CreateAsync(character, CancellationToken);

        Assert.Equal(CharacterCreationStoreStatus.Created, result.Status);
        Assert.True(CharacterIdentityPolicy.IsPlayerEntityId(result.CharacterId));

        ICharacterLoginProfileRepository repository = database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

        CharacterLoginProfile profile = Assert.IsType<CharacterLoginProfile>(
            await repository.FindByAccountIdAsync(accountId, CancellationToken));

        Assert.Equal(result.CharacterId, profile.Identity.CharacterId);
        Assert.Equal(character.AccountId, profile.Identity.AccountId);
        Assert.Equal(character.Name, profile.Identity.Name);

        Assert.Equal(character.AppearanceComposite, profile.Appearance.Composite);
        Assert.Equal(character.HairComposite, profile.Appearance.Hair);

        Assert.Equal(CharacterCreationCandidate.StartingLevel, profile.Progression.Level);
        Assert.Equal(0UL, profile.Progression.Experience);
        Assert.Equal(character.Profession, profile.Progression.Profession);
        Assert.Equal(character.Profession, profile.Progression.FirstProfession);
        Assert.Equal(character.Profession, profile.Progression.PreviousProfession);
        Assert.Equal((byte)0, profile.Progression.RebirthCount);
        Assert.Equal((byte)0, profile.Progression.PreRebirthLevel);

        Assert.Equal(character.Strength, profile.Attributes.Strength);
        Assert.Equal(character.Agility, profile.Attributes.Agility);
        Assert.Equal(character.Vitality, profile.Attributes.Vitality);
        Assert.Equal(character.Spirit, profile.Attributes.Spirit);
        Assert.Equal((ushort)0, profile.Attributes.UnspentPoints);

        Assert.Equal(character.CurrentLife, profile.Vitals.Life);
        Assert.Equal(character.CurrentMana, profile.Vitals.Mana);

        Assert.Equal(0u, profile.Economy.Silver);
        Assert.Equal(0u, profile.Economy.ConquerPoints);
        Assert.Equal(0u, profile.Economy.BoundConquerPoints);

        Assert.Equal((short)0, profile.PkPoints);
        Assert.Equal((ushort)0, profile.TitleId);
        Assert.Equal((ushort)0, profile.EnlightenmentPoints);

        Assert.Equal(character.MapId, profile.Location.MapId);
        Assert.Equal(character.PositionX, profile.Location.X);
        Assert.Equal(character.PositionY, profile.Location.Y);
    }

    [Fact]
    public async Task CreateAsync_DuplicateAccount_ReturnsConflictWithoutCreatingAnotherCharacter()
    {
        uint accountId = CreateAccountId();
        ICharacterCreationStore store = CreateStore();

        CharacterCreationStoreResult first = await store.CreateAsync(
            CreateCandidate(accountId, CreateCharacterName()), CancellationToken);

        CharacterCreationStoreResult second = await store.CreateAsync(
            CreateCandidate(accountId, CreateCharacterName()), CancellationToken);

        Assert.Equal(CharacterCreationStoreStatus.Created, first.Status);
        Assert.Equal(CharacterCreationStoreStatus.AccountAlreadyHasCharacter, second.Status);
        Assert.Equal(0u, second.CharacterId);

        Assert.Equal(1, await CountCharactersAsync(accountId));
    }

    [Fact]
    public async Task CreateAsync_RepeatedIdenticalSubmission_DoesNotReportSecondCreation()
    {
        CharacterCreationCandidate character = CreateCandidate(CreateAccountId(), CreateCharacterName());
        ICharacterCreationStore store = CreateStore();

        CharacterCreationStoreResult first = await store.CreateAsync(character, CancellationToken);
        CharacterCreationStoreResult second = await store.CreateAsync(character, CancellationToken);

        Assert.Equal(CharacterCreationStoreStatus.Created, first.Status);
        Assert.Equal(CharacterCreationStoreStatus.AccountAlreadyHasCharacter, second.Status);
        Assert.Equal(0u, second.CharacterId);

        Assert.Equal(1, await CountCharactersAsync(character.AccountId));
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsNameConflict()
    {
        string name = CreateCharacterName();
        ICharacterCreationStore store = CreateStore();

        CharacterCreationStoreResult first = await store.CreateAsync(
            CreateCandidate(CreateAccountId(), name), CancellationToken);

        uint secondAccountId = CreateAccountId();

        CharacterCreationStoreResult second = await store.CreateAsync(
            CreateCandidate(secondAccountId, name), CancellationToken);

        Assert.Equal(CharacterCreationStoreStatus.Created, first.Status);
        Assert.Equal(CharacterCreationStoreStatus.NameAlreadyInUse, second.Status);
        Assert.Equal(0u, second.CharacterId);

        Assert.Equal(0, await CountCharactersAsync(secondAccountId));
    }

    [Fact]
    public async Task CreateAsync_ConcurrentSameAccount_OnlyOneInsertSucceeds()
    {
        uint accountId = CreateAccountId();
        ICharacterCreationStore store = CreateStore();

        Task<CharacterCreationStoreResult>[] requests =
        [
            store.CreateAsync(CreateCandidate(accountId, CreateCharacterName()), CancellationToken).AsTask(),
            store.CreateAsync(CreateCandidate(accountId, CreateCharacterName()), CancellationToken).AsTask(),
        ];

        CharacterCreationStoreResult[] results = await Task.WhenAll(requests);

        Assert.Single(results, result => result.Status == CharacterCreationStoreStatus.Created);
        Assert.Single(results, result => result.Status == CharacterCreationStoreStatus.AccountAlreadyHasCharacter);

        Assert.Equal(1, await CountCharactersAsync(accountId));
    }

    [Fact]
    public async Task CreateAsync_ConcurrentSameName_OnlyOneInsertSucceeds()
    {
        string name = CreateCharacterName();
        uint firstAccountId = CreateAccountId();
        uint secondAccountId = CreateAccountId();
        ICharacterCreationStore store = CreateStore();

        Task<CharacterCreationStoreResult>[] requests =
        [
            store.CreateAsync(CreateCandidate(firstAccountId, name), CancellationToken).AsTask(),
            store.CreateAsync(CreateCandidate(secondAccountId, name), CancellationToken).AsTask(),
        ];

        CharacterCreationStoreResult[] results = await Task.WhenAll(requests);

        Assert.Single(results, result => result.Status == CharacterCreationStoreStatus.Created);
        Assert.Single(results, result => result.Status == CharacterCreationStoreStatus.NameAlreadyInUse);

        Assert.Equal(1, await CountCharactersAsync(firstAccountId) + await CountCharactersAsync(secondAccountId));
    }

    [Fact]
    public async Task CreateAsync_PreCanceledOperation_DoesNotInsert()
    {
        uint accountId = CreateAccountId();
        ICharacterCreationStore store = CreateStore();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.CreateAsync(CreateCandidate(accountId, CreateCharacterName()), cancellation.Token));

        Assert.Equal(0, await CountCharactersAsync(accountId));
    }

    [Fact]
    public async Task CreationIdentity_CanInsertButCannotUpdateOrDeleteCharacters()
    {
        uint accountId = CreateAccountId();
        ICharacterCreationStore store = CreateStore();

        CharacterCreationStoreResult result = await store.CreateAsync(
            CreateCandidate(accountId, CreateCharacterName()), CancellationToken);

        Assert.Equal(CharacterCreationStoreStatus.Created, result.Status);

        await using MySqlConnection connection = new(database.CreationConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using (MySqlCommand update = connection.CreateCommand())
        {
            update.CommandText =
                """
                UPDATE `characters`
                SET `silver` = 1
                WHERE `account_id` = @account_id
                """;

            update.Parameters.AddWithValue("@account_id", accountId);

            MySqlException exception = await Assert.ThrowsAsync<MySqlException>(
                () => update.ExecuteNonQueryAsync(CancellationToken));

            Assert.Equal(1142, exception.Number);
        }

        await using (MySqlCommand delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM `characters` WHERE `account_id` = @account_id";
            delete.Parameters.AddWithValue("@account_id", accountId);

            MySqlException exception = await Assert.ThrowsAsync<MySqlException>(
                () => delete.ExecuteNonQueryAsync(CancellationToken));

            Assert.Equal(1142, exception.Number);
        }

        Assert.Equal(1, await CountCharactersAsync(accountId));
    }

    [Fact]
    public void AddGameCharacterCreationPersistence_RegistersCreationStore()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddGameCharacterCreationPersistence(database.CreationConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.IsType<CharacterCreationStore>(services.GetRequiredService<ICharacterCreationStore>());
    }

    [Fact]
    public void AddGameCharacterCreationPersistence_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GameCharacterCreationPersistenceServiceCollectionExtensions.AddGameCharacterCreationPersistence(
                null!, database.CreationConnectionString));

        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddGameCharacterCreationPersistence(" "));
    }

    private ICharacterCreationStore CreateStore()
    {
        return new CharacterCreationStore(database.CreationConnectionString);
    }

    private async Task<int> CountCharactersAsync(uint accountId)
    {
        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM `characters`
            WHERE `account_id` = @account_id
            """;

        command.Parameters.AddWithValue("@account_id", accountId);

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        return Convert.ToInt32(result);
    }

    private static CharacterCreationCandidate CreateCandidate(uint accountId, string name)
    {
        return new CharacterCreationCandidate(
            accountId,
            name,
            body: 1003,
            profession: 10,
            appearanceComposite: 2_011_003,
            hairComposite: 456,
            strength: 4,
            agility: 1,
            vitality: 4,
            spirit: 1,
            currentMana: 0,
            mapId: 1002,
            positionX: 431,
            positionY: 379);
    }

    private static uint CreateAccountId()
    {
        return checked((uint)Interlocked.Increment(ref s_nextAccountId));
    }

    private static string CreateCharacterName()
    {
        return "Create" + Guid.NewGuid().ToString("N")[..9];
    }
}
