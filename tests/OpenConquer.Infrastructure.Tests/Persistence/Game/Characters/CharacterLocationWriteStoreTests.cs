using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Infrastructure.DependencyInjection;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game.Characters;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterLocationWriteStoreTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 1_600_000;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commit_ChangesOnlyTheRequestedCharacterLocation()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationWriteStore writer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);
        CharacterLocation destination = new(1011, 193, 266);

        CharacterLocationWriteResult result = await writer.TryCommitAsync(
            Change(account, character, new CharacterLocation(1002, 431, 379), destination), CancellationToken);

        Assert.Equal(CharacterLocationWriteResult.Committed, result);
        CharacterLoginProfile profile = await LoadAsync(account);
        Assert.Equal(destination.MapId, profile.Location.MapId);
        Assert.Equal(destination.X, profile.Location.X);
        Assert.Equal(destination.Y, profile.Location.Y);
        Assert.Equal((ushort)177, profile.Vitals.Life); // Writer cannot modify vitals or inventory.
    }

    [Fact]
    public async Task Commit_StaleExpectedLocationAndForeignAccountCannotOverwrite()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationWriteStore writer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);
        CharacterLocation original = new(1002, 431, 379);
        CharacterLocation destination = new(1011, 193, 266);
        Assert.Equal(CharacterLocationWriteResult.Committed,
            await writer.TryCommitAsync(Change(account, character, original, destination), CancellationToken));

        Assert.Equal(CharacterLocationWriteResult.Conflict,
            await writer.TryCommitAsync(Change(account, character, original, new CharacterLocation(1000, 499, 650)), CancellationToken));
        Assert.Equal(CharacterLocationWriteResult.Conflict,
            await writer.TryCommitAsync(Change(account + 1, character, destination, new CharacterLocation(1000, 499, 650)), CancellationToken));

        CharacterLoginProfile profile = await LoadAsync(account);
        Assert.Equal((1011u, (ushort)193, (ushort)266), (profile.Location.MapId, profile.Location.X, profile.Location.Y));
    }

    [Fact]
    public async Task Commit_ConcurrentClaimsForOneExpectedLocation_ExactlyOneWins()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationWriteStore writer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);
        CharacterLocation original = new(1002, 431, 379);
        CharacterLocation first = new(1011, 193, 266);
        CharacterLocation second = new(1000, 499, 650);

        Task<CharacterLocationWriteResult> firstWrite = writer.TryCommitAsync(
            Change(account, character, original, first), CancellationToken).AsTask();
        Task<CharacterLocationWriteResult> secondWrite = writer.TryCommitAsync(
            Change(account, character, original, second), CancellationToken).AsTask();
        CharacterLocationWriteResult[] results = await Task.WhenAll(firstWrite, secondWrite);

        Assert.Single(results, result => result == CharacterLocationWriteResult.Committed);
        Assert.Single(results, result => result == CharacterLocationWriteResult.Conflict);
        CharacterLoginProfile profile = await LoadAsync(account);
        Assert.True((profile.Location.MapId == first.MapId && profile.Location.X == first.X && profile.Location.Y == first.Y) ||
            (profile.Location.MapId == second.MapId && profile.Location.X == second.X && profile.Location.Y == second.Y));
    }

    [Fact]
    public async Task Commit_ReturnToSameCoordinate_DoesNotAllowReplayedOldRevision()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        CharacterLocationWriteStore store = new(database.LocationWriterConnectionString);
        CharacterLocation original = new(1002, 431, 379);
        CharacterLocation other = new(1011, 193, 266);

        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(Change(account, character, original, other, 0), CancellationToken));
        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(Change(account, character, other, original, 1), CancellationToken));

        PersistedCharacterLocation after = Assert.IsType<PersistedCharacterLocation>(
            await store.ObserveAsync(account, character, CancellationToken));
        Assert.Equal(2, after.Revision);
        Assert.Equal(original.MapId, after.Location.MapId);
        Assert.Equal(original.X, after.Location.X);
        Assert.Equal(original.Y, after.Location.Y);

        Assert.Equal(CharacterLocationWriteResult.Conflict,
            await store.TryCommitAsync(Change(account, character, original, other, 0), CancellationToken));
        Assert.Equal(2, Assert.IsType<PersistedCharacterLocation>(
            await store.ObserveAsync(account, character, CancellationToken)).Revision);
    }

    [Fact]
    public async Task Commit_MarkerIsScopedToTheWinningAtomicRevision()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        CharacterLocationWriteStore store = new(database.LocationWriterConnectionString);
        CharacterLocation original = new(1002, 431, 379);
        CharacterLocation destination = new(1011, 193, 266);
        CharacterLocation alternate = new(1000, 499, 650);
        CharacterLocationChange winner = Change(account, character, original, destination);
        CharacterLocationChange stale = Change(account, character, original, alternate);

        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(winner, CancellationToken));
        Assert.Equal(CharacterLocationWriteResult.Conflict,
            await store.TryCommitAsync(stale, CancellationToken));

        PersistedCharacterLocation snapshot = Assert.IsType<PersistedCharacterLocation>(
            await store.ObserveAsync(account, character, CancellationToken));
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(winner.OperationId, snapshot.LastOperationId);
        Assert.True(CharacterLocationCommitEvidence.IsConfirmed(winner, snapshot));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(stale, snapshot));
        Assert.Null(await store.ObserveAsync(account + 1, character, CancellationToken));
    }

    [Fact]
    public async Task RuntimeReadonlyIdentity_CannotCommitCharacterLocation()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationWriteStore writer = new CharacterLocationWriteStore(database.RuntimeConnectionString);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.TryCommitAsync(Change(account, character, new CharacterLocation(1002, 431, 379),
                new CharacterLocation(1011, 193, 266)), CancellationToken));

        MySqlException denied = Assert.IsType<MySqlException>(failure.InnerException);
        Assert.Contains(denied.Number, new[] { 1142, 1143 });
        CharacterLoginProfile profile = await LoadAsync(account);
        Assert.Equal(1002u, profile.Location.MapId);
    }

    [Fact]
    public async Task LocationWriter_CannotWriteVitalsOrReadUnrelatedColumns()
    {
        (uint account, uint character) = await CreateCharacterAsync();

        await using MySqlConnection connection = new(database.LocationWriterConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using (MySqlCommand update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE `characters`
                SET `current_life` = 1
                WHERE `character_id` = @character_id
                  AND `account_id` = @account_id
                """;
            update.Parameters.AddWithValue("@character_id", character);
            update.Parameters.AddWithValue("@account_id", account);

            MySqlException denial = await Assert.ThrowsAsync<MySqlException>(
                () => update.ExecuteNonQueryAsync(CancellationToken));
            Assert.Contains(denial.Number, new[] { 1142, 1143 });
        }

        await using (MySqlCommand read = connection.CreateCommand())
        {
            read.CommandText = "SELECT `current_life` FROM `characters` WHERE `character_id` = @character_id";
            read.Parameters.AddWithValue("@character_id", character);

            MySqlException denial = await Assert.ThrowsAsync<MySqlException>(
                () => read.ExecuteScalarAsync(CancellationToken));
            Assert.Contains(denial.Number, new[] { 1142, 1143 });
        }

        Assert.Equal((ushort)177, (await LoadAsync(account)).Vitals.Life);
    }

    [Fact]
    public async Task Observe_ReflectsPersistedLocationBeforeAndAfterCommit()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        CharacterLocationWriteStore store = new(database.LocationWriterConnectionString);
        ICharacterLocationObservationStore observer = store;
        PersistedCharacterLocation before = Assert.IsType<PersistedCharacterLocation>(
            await observer.ObserveAsync(account, character, CancellationToken));
        Assert.Equal((1002u, (ushort)431, (ushort)379), (before.Location.MapId, before.Location.X, before.Location.Y));
        Assert.Equal(0, before.Revision);
        Assert.Null(before.LastOperationId);

        CharacterLocation destination = new(1011, 193, 266);
        CharacterLocationChange change = Change(account, character, before.Location, destination, before.Revision);
        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(change, CancellationToken));

        PersistedCharacterLocation after = Assert.IsType<PersistedCharacterLocation>(
            await observer.ObserveAsync(account, character, CancellationToken));
        Assert.Equal((destination.MapId, destination.X, destination.Y), (after.Location.MapId, after.Location.X, after.Location.Y));
        Assert.Equal(1, after.Revision);
        Assert.Equal(change.OperationId, after.LastOperationId);
        Assert.True(CharacterLocationCommitEvidence.IsConfirmed(change, after));
        Assert.Equal((ushort)177, (await LoadAsync(account)).Vitals.Life);
    }

    [Fact]
    public async Task ReceiptEncoding_IsIndependentOfConfiguredGuidFormat()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        MySqlConnectionStringBuilder credentials = new(database.LocationWriterConnectionString)
        {
            GuidFormat = MySqlGuidFormat.Binary16,
        };
        CharacterLocationWriteStore store = new(credentials.ConnectionString);
        CharacterLocationChange change = Change(account, character,
            new CharacterLocation(1002, 431, 379), new CharacterLocation(1011, 193, 266));
        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(change, CancellationToken));
        PersistedCharacterLocation snapshot = Assert.IsType<PersistedCharacterLocation>(
            await store.ObserveAsync(account, character, CancellationToken));
        Assert.True(CharacterLocationCommitEvidence.IsConfirmed(change, snapshot));
    }

    [Fact]
    public async Task Observe_RefusesForeignAccountAndMissingCharacterWithoutDisclosingLocation()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationObservationStore observer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);

        Assert.Null(await observer.ObserveAsync(account + 1, character, CancellationToken));
        Assert.Null(await observer.ObserveAsync(account, uint.MaxValue, CancellationToken));
        Assert.NotNull(await observer.ObserveAsync(account, character, CancellationToken));
    }

    [Fact]
    public async Task Observe_RejectsInvalidIdentitiesAndPreCancelledReads()
    {
        ICharacterLocationObservationStore observer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await observer.ObserveAsync(0, 1_000_000, CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await observer.ObserveAsync(1, 999_999, CancellationToken));
        using CancellationTokenSource stopped = new();
        stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await observer.ObserveAsync(1, 1_000_000, stopped.Token));
    }

    [Fact]
    public async Task Commit_NearRevisionLimit_StopsWithoutWrapping()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using (MySqlCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE `characters` SET `location_revision` = @revision WHERE `character_id` = @character";
            command.Parameters.AddWithValue("@revision", long.MaxValue - 1);
            command.Parameters.AddWithValue("@character", character);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
        }
        CharacterLocationWriteStore store = new(database.LocationWriterConnectionString);
        CharacterLocation a = new(1002, 431, 379);
        CharacterLocation b = new(1011, 193, 266);
        Assert.Equal(CharacterLocationWriteResult.Committed,
            await store.TryCommitAsync(Change(account, character, a, b, long.MaxValue - 1), CancellationToken));
        Assert.Equal(long.MaxValue, Assert.IsType<PersistedCharacterLocation>(
            await store.ObserveAsync(account, character, CancellationToken)).Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() => Change(account, character, b, a, long.MaxValue));
    }

    [Fact]
    public async Task Commit_PreCanceledOperation_DoesNotDispatch()
    {
        (uint account, uint character) = await CreateCharacterAsync();
        ICharacterLocationWriteStore writer = new CharacterLocationWriteStore(database.LocationWriterConnectionString);
        using CancellationTokenSource stopped = new();
        stopped.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await writer.TryCommitAsync(Change(account, character, new CharacterLocation(1002, 431, 379),
                new CharacterLocation(1011, 193, 266)), stopped.Token));

        Assert.Equal(1002u, (await LoadAsync(account)).Location.MapId);
    }

    [Fact]
    public void Registration_RequiresExplicitWriterCredentials()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddGameCharacterLocationWrite(database.LocationWriterConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        ICharacterLocationWriteStore writer = services.GetRequiredService<ICharacterLocationWriteStore>();
        ICharacterLocationObservationStore observer = services.GetRequiredService<ICharacterLocationObservationStore>();
        Assert.IsType<CharacterLocationWriteStore>(writer);
        Assert.Same(writer, observer);
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGameCharacterLocationWrite(" "));
    }

    private async Task<(uint Account, uint Character)> CreateCharacterAsync()
    {
        uint account = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        CharacterCreationCandidate candidate = new(account, "Loc" + Guid.NewGuid().ToString("N")[..10],
            body: 1003, profession: 10, appearanceComposite: 2_011_003, hairComposite: 410,
            strength: 1, agility: 1, vitality: 7, spirit: 1, currentMana: 0,
            mapId: 1002, positionX: 431, positionY: 379);
        CharacterCreationStore creator = new(database.AdministrativeConnectionString);
        CharacterCreationStoreResult result = await creator.CreateAsync(candidate, CancellationToken);
        Assert.Equal(CharacterCreationStoreStatus.Created, result.Status);
        return (account, result.CharacterId);
    }

    private async Task<CharacterLoginProfile> LoadAsync(uint account)
    {
        ICharacterLoginProfileRepository reader = database.Services.GetRequiredService<ICharacterLoginProfileRepository>();
        return Assert.IsType<CharacterLoginProfile>(await reader.FindByAccountIdAsync(account, CancellationToken));
    }

    private static CharacterLocationChange Change(uint account, uint character, CharacterLocation expected,
        CharacterLocation destination, long expectedRevision = 0) =>
        new(account, character, expected, destination, expectedRevision, Guid.NewGuid());
}
