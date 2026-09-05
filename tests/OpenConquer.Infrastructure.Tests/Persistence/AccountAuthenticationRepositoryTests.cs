using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Security;
using OpenConquer.Infrastructure.Tests.Security;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountAuthenticationRepositoryTests(AccountDatabaseFixture database)
    : IClassFixture<AccountDatabaseFixture>
{
    private const string OriginalHash = "$openconquer$original$HashValue";
    private const string ReplacementHash = "$openconquer$replacement$HashValue";
    private const uint Timestamp = 1_788_566_400;

    private IAccountAuthenticationRepository Repository => database.Services.GetRequiredService<IAccountAuthenticationRepository>();
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_RejectsNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountAuthenticationRepository(null!));
    }

    [Fact]
    public async Task FindByNameAsync_RejectsNullName()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Repository.FindByNameAsync(null!, CancellationToken).AsTask());
    }

    [Fact]
    public async Task FindByNameAsync_MissingAndSqlInjectionNamesReturnNull()
    {
        Assert.Null(await Repository.FindByNameAsync(Guid.NewGuid().ToString("N"), CancellationToken));
        Assert.Null(await Repository.FindByNameAsync("' OR 1=1; --", CancellationToken));
    }

    [Theory]
    [InlineData(0u, AccountLoginAccess.Denied)]
    [InlineData(1u, AccountLoginAccess.Allowed)]
    [InlineData(2u, AccountLoginAccess.Allowed)]
    [InlineData(3u, AccountLoginAccess.Allowed)]
    [InlineData(4u, AccountLoginAccess.Allowed)]
    [InlineData(5u, AccountLoginAccess.Allowed)]
    [InlineData(255u, AccountLoginAccess.Banned)]
    public async Task FindByNameAsync_MapsPermissionAndPreservesCanonicalName(uint permission, AccountLoginAccess access)
    {
        AccountRecord record = await InsertAccountAsync(permission: permission);
        AccountAuthenticationSnapshot? snapshot = await Repository.FindByNameAsync(record.Username.ToUpperInvariant(), CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(record.Id, snapshot.AccountId);
        Assert.Equal(record.Username, snapshot.Username);
        Assert.Equal(OriginalHash, snapshot.PasswordHash);
        Assert.Equal(access, snapshot.Access);
    }

    [Theory]
    [InlineData(6u)]
    [InlineData(256u)]
    public async Task FindByNameAsync_UnknownPersistedPermissionIsDenied(uint permission)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        // The baseline's FK requires a catalogue entry before a new permission can be persisted.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO player_permission_types (permission_code, name) VALUES ({permission}, {"Unknown" + permission})",
            CancellationToken);
        AccountRecord record = await InsertAccountAsync(permission: permission);
        Assert.Equal(AccountLoginAccess.Denied, (await Repository.FindByNameAsync(record.Username, CancellationToken))!.Access);
    }

    [Fact]
    public async Task FindByNameAsync_InvalidStoredHashFailsClosed()
    {
        AccountRecord record = await InsertAccountAsync("   ");
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.FindByNameAsync(record.Username, CancellationToken).AsTask());
    }

    [Fact]
    public async Task FindByNameAsync_DuplicateNamesInDamagedSchemaFailClosed()
    {
        AccountRecord original = await InsertAccountAsync();
        AccountRecord duplicate = await InsertAccountAsync();
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE accounts DROP INDEX UX_accounts_username", CancellationToken);
        try
        {
            await db.Accounts.Where(account => account.Id == duplicate.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(account => account.Username, original.Username.ToUpperInvariant()), CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.FindByNameAsync(original.Username, CancellationToken).AsTask());
        }
        finally
        {
            await db.Accounts.Where(account => account.Id == duplicate.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE accounts ADD UNIQUE INDEX UX_accounts_username (username)", CancellationToken.None);
        }
    }

    [Fact]
    public async Task FindByNameAsync_ReadsCurrentStateAcrossPooledContexts()
    {
        AccountRecord record = await InsertAccountAsync();
        Assert.Equal(OriginalHash, (await Repository.FindByNameAsync(record.Username, CancellationToken))!.PasswordHash);
        await UpdateAccountAsync(record.Id, ReplacementHash, permission: 255);

        AccountAuthenticationSnapshot? refreshed = await Repository.FindByNameAsync(record.Username, CancellationToken);
        Assert.Equal(ReplacementHash, refreshed!.PasswordHash);
        Assert.Equal(AccountLoginAccess.Banned, refreshed.Access);
    }

    [Fact]
    public async Task TryRecordLoginAsync_UpdatesHashAndTimestampWithoutChangingOtherFields()
    {
        AccountRecord record = await InsertAccountAsync();
        Assert.True(await Repository.TryRecordLoginAsync(Snapshot(record), ReplacementHash, Timestamp, CancellationToken));
        AccountRecord saved = await ReadAccountAsync(record.Id);

        Assert.Equal(ReplacementHash, saved.PasswordHash);
        Assert.Equal(Timestamp, saved.LoginTimestamp);
        AssertUnchangedAccountFields(record, saved);
    }

    [Fact]
    public async Task TryRecordLoginAsync_CurrentHashLoginAndSameSecondLoginBothSucceed()
    {
        AccountRecord record = await InsertAccountAsync();
        AccountAuthenticationSnapshot snapshot = Snapshot(record);
        Assert.True(await Repository.TryRecordLoginAsync(snapshot, null, Timestamp, CancellationToken));
        Assert.True(await Repository.TryRecordLoginAsync(snapshot, null, Timestamp, CancellationToken));
        Assert.Equal(OriginalHash, (await ReadAccountAsync(record.Id)).PasswordHash);
    }

    [Theory]
    [InlineData("$openconquer$original$hashvalue")]
    [InlineData(OriginalHash + " ")]
    [InlineData(" " + OriginalHash)]
    public async Task TryRecordLoginAsync_HashComparisonIsByteExact(string expectedHash)
    {
        AccountRecord record = await InsertAccountAsync();
        AccountAuthenticationSnapshot snapshot = new(record.Id, record.Username, expectedHash, AccountLoginAccess.Allowed);
        Assert.False(await Repository.TryRecordLoginAsync(snapshot, ReplacementHash, Timestamp, CancellationToken));
        AccountRecord saved = await ReadAccountAsync(record.Id);
        Assert.Equal(OriginalHash, saved.PasswordHash);
        Assert.Equal(0u, saved.LoginTimestamp);
    }

    [Fact]
    public async Task TryRecordLoginAsync_ConcurrentMigrationHasExactlyOneWinner()
    {
        AccountRecord record = await InsertAccountAsync();
        AccountAuthenticationSnapshot snapshot = Snapshot(record);
        bool[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            Repository.TryRecordLoginAsync(snapshot, ReplacementHash + index, Timestamp, CancellationToken).AsTask()));
        Assert.Single(results, succeeded => succeeded);
        Assert.StartsWith(ReplacementHash, (await ReadAccountAsync(record.Id)).PasswordHash);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(255u)]
    public async Task TryRecordLoginAsync_ConcurrentAccessChangePreventsSuccessfulLogin(uint permission)
    {
        AccountRecord record = await InsertAccountAsync();
        await UpdateAccountAsync(record.Id, OriginalHash, permission);
        Assert.False(await Repository.TryRecordLoginAsync(Snapshot(record), ReplacementHash, Timestamp, CancellationToken));
        Assert.Equal(0u, (await ReadAccountAsync(record.Id)).LoginTimestamp);
    }

    [Fact]
    public async Task TryRecordLoginAsync_ConcurrentRenameRejectsStaleCanonicalName()
    {
        AccountRecord record = await InsertAccountAsync();
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Accounts.Where(account => account.Id == record.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(account => account.Username, record.Username.ToUpperInvariant()), CancellationToken);
        Assert.False(await Repository.TryRecordLoginAsync(Snapshot(record), null, Timestamp, CancellationToken));
    }

    [Fact]
    public async Task TryRecordLoginAsync_DeletedAccountReturnsFalse()
    {
        AccountRecord record = await InsertAccountAsync();
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Accounts.Where(account => account.Id == record.Id).ExecuteDeleteAsync(CancellationToken);
        Assert.False(await Repository.TryRecordLoginAsync(Snapshot(record), ReplacementHash, Timestamp, CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TryRecordLoginAsync_RejectsEmptyReplacement(string replacement)
    {
        AccountRecord record = await InsertAccountAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.TryRecordLoginAsync(Snapshot(record), replacement, Timestamp, CancellationToken).AsTask());
    }

    [Fact]
    public async Task TryRecordLoginAsync_RejectsOversizedHashesAndNullSnapshot()
    {
        AccountRecord record = await InsertAccountAsync();
        await Assert.ThrowsAsync<ArgumentNullException>(() => Repository.TryRecordLoginAsync(null!, null, Timestamp, CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.TryRecordLoginAsync(Snapshot(record), new string('x', 256), Timestamp, CancellationToken).AsTask());
        AccountAuthenticationSnapshot invalid = new(record.Id, record.Username, new string('x', 256), AccountLoginAccess.Allowed);
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.TryRecordLoginAsync(invalid, null, Timestamp, CancellationToken).AsTask());
    }

    [Fact]
    public async Task Operations_ObserveCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository.FindByNameAsync("Account", cancellation.Token).AsTask());
        AccountAuthenticationSnapshot snapshot = new(1, "Account", OriginalHash, AccountLoginAccess.Allowed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository.TryRecordLoginAsync(snapshot, null, Timestamp, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task AuthenticateAsync_MigratesIdentityV3AndPersistsEverySuccessfulLogin()
    {
        AccountRecord record = await InsertAccountAsync(PasswordHashMigrationTests.CreateIdentityV3Hash("Test1234"));
        AccountPasswordHasher hasher = new();
        AccountAuthenticator authenticator = new(Repository, hasher, new AttemptLimiter(), new FixedTimeProvider());

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync("  " + record.Username.ToUpperInvariant() + "  ",
            "Test1234".AsMemory(), IPAddress.Loopback, CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(record.Username, result.Username);
        AccountRecord saved = await ReadAccountAsync(record.Id);
        Assert.Equal(Timestamp, saved.LoginTimestamp);
        Assert.Equal(AccountPasswordVerificationStatus.Success, hasher.VerifyPassword(saved.PasswordHash, "Test1234"));
        AssertUnchangedAccountFields(record, saved);

        Assert.True((await authenticator.AuthenticateAsync(record.Username, "Test1234".AsMemory(), IPAddress.Loopback, CancellationToken)).IsSuccess);
        Assert.Equal(saved.PasswordHash, (await ReadAccountAsync(record.Id)).PasswordHash);
    }

    [Theory]
    [InlineData(1u, "wrong", AccountAuthenticationStatus.InvalidCredentials)]
    [InlineData(255u, "wrong", AccountAuthenticationStatus.InvalidCredentials)]
    [InlineData(255u, "Test1234", AccountAuthenticationStatus.Banned)]
    [InlineData(0u, "Test1234", AccountAuthenticationStatus.InvalidCredentials)]
    public async Task AuthenticateAsync_FailedOrDeniedLoginDoesNotWriteAccount(uint permission, string password,
        AccountAuthenticationStatus expected)
    {
        AccountRecord record = await InsertAccountAsync(PasswordHashMigrationTests.CreateIdentityV3Hash("Test1234"), permission);
        AccountAuthenticator authenticator = new(Repository, new AccountPasswordHasher(), new AttemptLimiter(), new FixedTimeProvider());
        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(record.Username, password.AsMemory(),
            IPAddress.Loopback, CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Username);
        AccountRecord saved = await ReadAccountAsync(record.Id);
        Assert.Equal(record.PasswordHash, saved.PasswordHash);
        Assert.Equal(0u, saved.LoginTimestamp);
        AssertUnchangedAccountFields(record, saved);
    }

    [Theory]
    [InlineData(false, 1u, AccountAuthenticationStatus.Success)]
    [InlineData(true, 1u, AccountAuthenticationStatus.InvalidCredentials)]
    [InlineData(false, 255u, AccountAuthenticationStatus.Banned)]
    [InlineData(false, 0u, AccountAuthenticationStatus.InvalidCredentials)]
    public async Task AuthenticateAsync_RevalidatesConcurrentPasswordAndAccessChanges(bool resetPassword, uint permission,
        AccountAuthenticationStatus expected)
    {
        AccountPasswordHasher hasher = new();
        AccountRecord record = await InsertAccountAsync(PasswordHashMigrationTests.CreateIdentityV3Hash("Test1234"));
        string concurrentHash = hasher.HashPassword(resetPassword ? "new-password" : "Test1234");
        ConflictingRepository repository = new(Repository, () => UpdateAccountAsync(record.Id, concurrentHash, permission));
        AccountAuthenticator authenticator = new(repository, hasher, new AttemptLimiter(), new FixedTimeProvider());

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(record.Username, "Test1234".AsMemory(), IPAddress.Loopback, CancellationToken);
        Assert.Equal(expected, result.Status);
        AccountRecord saved = await ReadAccountAsync(record.Id);
        Assert.Equal(concurrentHash, saved.PasswordHash);
        Assert.Equal(expected == AccountAuthenticationStatus.Success ? Timestamp : 0u, saved.LoginTimestamp);
    }

    [Fact]
    public async Task AuthenticateAsync_RecoversWhenMigrationCommitsBeforeTransientAcknowledgementFailure()
    {
        AccountRecord record = await InsertAccountAsync(PasswordHashMigrationTests.CreateIdentityV3Hash("Test1234"));
        CommitAcknowledgementFailure failure = new();
        ServiceCollection services = new();
        services.AddAccountPersistence(database.ConnectionString);
        services.ConfigureDbContext<AccountDbContext>(options => options.AddInterceptors(failure));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IAccountAuthenticationRepository repository = provider.GetRequiredService<IAccountAuthenticationRepository>();
        AccountPasswordHasher hasher = new();
        AccountAuthenticator authenticator = new(repository, hasher, new AttemptLimiter(), new FixedTimeProvider());

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(record.Username, "Test1234".AsMemory(),
            IPAddress.Loopback, CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(failure.Injected);
        Assert.Equal(3, failure.UpdateCount);
        AccountRecord saved = await ReadAccountAsync(record.Id);
        Assert.Equal(Timestamp, saved.LoginTimestamp);
        Assert.Equal(AccountPasswordVerificationStatus.Success, hasher.VerifyPassword(saved.PasswordHash, "Test1234"));
    }

    private sealed class CommitAcknowledgementFailure : DbCommandInterceptor
    {
        public bool Injected { get; private set; }
        public int UpdateCount { get; private set; }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                UpdateCount++;
                if (!Injected)
                {
                    Injected = true;
                    throw new TimeoutException("Simulated lost acknowledgement after a committed update.");
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    private async Task<AccountRecord> InsertAccountAsync(string hash = OriginalHash, uint permission = 1)
    {
        AccountRecord account = new()
        {
            Username = Guid.NewGuid().ToString("N"),
            PasswordHash = hash,
            Permission = permission,
            Email = "account@example.test",
            EmailVerification = "verification",
            EmailStatus = 1,
            SecurityQuestion = "question",
            SecurityAnswer = "answer",
            RegistrationOperationId = new string('a', 32) + Guid.NewGuid().ToString("N"),
        };
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(CancellationToken);
        return account;
    }

    private async Task<AccountRecord> ReadAccountAsync(uint id)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        return await db.Accounts.AsNoTracking().SingleAsync(account => account.Id == id, CancellationToken);
    }

    private async Task UpdateAccountAsync(uint id, string hash, uint permission)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Accounts.Where(account => account.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(account => account.PasswordHash, hash).SetProperty(account => account.Permission, permission), CancellationToken);
    }

    private static AccountAuthenticationSnapshot Snapshot(AccountRecord account)
    {
        return new(account.Id, account.Username, account.PasswordHash, AccountLoginAccess.Allowed);
    }

    private static void AssertUnchangedAccountFields(AccountRecord before, AccountRecord after)
    {
        Assert.Equal(before.Username, after.Username);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.EmailVerification, after.EmailVerification);
        Assert.Equal(before.EmailStatus, after.EmailStatus);
        Assert.Equal(before.SecurityQuestion, after.SecurityQuestion);
        Assert.Equal(before.SecurityAnswer, after.SecurityAnswer);
        Assert.Equal(before.Permission, after.Permission);
        Assert.Equal(before.RegistrationOperationId, after.RegistrationOperationId);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Timestamp);
    }

    private sealed class AttemptLimiter : IAccountAuthenticationAttemptLimiter
    {
        public bool TryBeginAuthentication(IPAddress remoteAddress, uint accountId,
            [NotNullWhen(true)] out IAccountAuthenticationAttemptLease? attempt)
        {
            attempt = new AttemptLease();
            return true;
        }
    }

    private sealed class AttemptLease : IAccountAuthenticationAttemptLease
    {
        public void Complete(bool credentialsAccepted) { }
        public void Dispose() { }
    }

    private sealed class ConflictingRepository(IAccountAuthenticationRepository repository, Func<Task> change)
        : IAccountAuthenticationRepository
    {
        private bool _changed;

        public ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => repository.FindByNameAsync(name, cancellationToken);

        public async ValueTask<bool> TryRecordLoginAsync(AccountAuthenticationSnapshot account, string? hash, uint timestamp,
            CancellationToken cancellationToken = default)
        {
            if (!_changed)
            {
                _changed = true;
                await change();
            }

            return await repository.TryRecordLoginAsync(account, hash, timestamp, cancellationToken);
        }
    }
}
