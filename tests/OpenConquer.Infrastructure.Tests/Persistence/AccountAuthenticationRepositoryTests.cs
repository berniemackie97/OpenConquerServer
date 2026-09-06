using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountAuthenticationRepositoryTests(AccountDatabaseFixture database)
    : IClassFixture<AccountDatabaseFixture>
{
    private const string OriginalPasswordHash = "$openconquer$original$HashValue";
    private const string ReplacementPasswordHash = "$openconquer$replacement$HashValue";

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IAccountAuthenticationRepository Repository =>
        database.Services.GetRequiredService<IAccountAuthenticationRepository>();

    [Fact]
    public void Constructor_RejectsNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountAuthenticationRepository(null!));
    }

    [Fact]
    public async Task FindByNameAsync_RejectsNullName()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Repository.FindByNameAsync(null!, CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task FindByNameAsync_MissingAndInjectionLikeNamesReturnNull()
    {
        Assert.Null(await Repository.FindByNameAsync(CreateUsername(), CancellationToken));

        Assert.Null(await Repository.FindByNameAsync("' OR 1=1; --", CancellationToken));
    }

    [Theory]
    [InlineData(AccountAccessStatus.Active, AccountLoginAccess.Allowed)]
    [InlineData(AccountAccessStatus.Suspended, AccountLoginAccess.Denied)]
    [InlineData(AccountAccessStatus.Banned, AccountLoginAccess.Banned)]
    public async Task FindByNameAsync_MapsAccessStatusAndPreservesCanonicalUsername(
        AccountAccessStatus accessStatus,
        AccountLoginAccess expectedAccess
    )
    {
        AccountRecord account = await InsertAccountAsync(accessStatus: accessStatus);

        AccountAuthenticationSnapshot? snapshot = await Repository.FindByNameAsync(
            account.Username.ToUpperInvariant(),
            CancellationToken
        );

        Assert.NotNull(snapshot);
        Assert.Equal(account.AccountId, snapshot.AccountId);
        Assert.Equal(account.Username, snapshot.Username);
        Assert.Equal(OriginalPasswordHash, snapshot.PasswordHash);
        Assert.Equal(expectedAccess, snapshot.Access);
        Assert.Equal(account.StateRevision, snapshot.AccountStateRevision);
        Assert.Equal(account.PasswordCredential.Revision, snapshot.PasswordCredentialRevision);
    }

    [Fact]
    public async Task FindByNameAsync_DeletedAccountIsDeniedRegardlessOfAccessStatus()
    {
        AccountRecord account = await InsertAccountAsync(accessStatus: AccountAccessStatus.Active);

        await DeleteAccountAsync(account.AccountId);

        AccountAuthenticationSnapshot? snapshot = await Repository.FindByNameAsync(
            account.Username,
            CancellationToken
        );

        Assert.NotNull(snapshot);
        Assert.Equal(AccountLoginAccess.Denied, snapshot.Access);
    }

    [Fact]
    public async Task FindByNameAsync_ReadsCurrentCredentialAndStateAcrossPooledContexts()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot first = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        Assert.Equal(OriginalPasswordHash, first.PasswordHash);
        Assert.Equal(1UL, first.AccountStateRevision);
        Assert.Equal(1UL, first.PasswordCredentialRevision);

        await SetAccessStatusAsync(account.AccountId, AccountAccessStatus.Banned);

        AccountAuthenticationSnapshot second = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        Assert.Equal(AccountLoginAccess.Banned, second.Access);
        Assert.Equal(2UL, second.AccountStateRevision);
        Assert.Equal(1UL, second.PasswordCredentialRevision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_RejectsNullSnapshot()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Repository
                .TryRecordSuccessfulLoginAsync(
                    null!,
                    null,
                    CreateSuccessfulLoginInstant(),
                    CancellationToken
                )
                .AsTask()
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TryRecordSuccessfulLoginAsync_RejectsEmptyReplacementHash(
        string replacementPasswordHash
    )
    {
        AccountAuthenticationSnapshot snapshot = await InsertAndFindAllowedSnapshotAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository
                .TryRecordSuccessfulLoginAsync(
                    snapshot,
                    replacementPasswordHash,
                    CreateSuccessfulLoginInstant(),
                    CancellationToken
                )
                .AsTask()
        );
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_RejectsOversizedReplacementHash()
    {
        AccountAuthenticationSnapshot snapshot = await InsertAndFindAllowedSnapshotAsync();

        string replacementPasswordHash = new('x', 256);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository
                .TryRecordSuccessfulLoginAsync(
                    snapshot,
                    replacementPasswordHash,
                    CreateSuccessfulLoginInstant(),
                    CancellationToken
                )
                .AsTask()
        );
    }

    [Theory]
    [InlineData(AccountLoginAccess.Denied)]
    [InlineData(AccountLoginAccess.Banned)]
    public async Task TryRecordSuccessfulLoginAsync_NonAllowedSnapshotDoesNotWrite(
        AccountLoginAccess access
    )
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = new(
            account.AccountId,
            account.Username,
            account.PasswordCredential.PasswordHash,
            access,
            account.StateRevision,
            account.PasswordCredential.Revision
        );

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                ReplacementPasswordHash,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Null(saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(OriginalPasswordHash, saved.PasswordCredential.PasswordHash);
        Assert.Equal(1UL, saved.PasswordCredential.Revision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_CurrentCredentialUpdatesOnlyLoginInstant()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        DateTimeOffset successfulLoginAt = new(2026, 1, 2, 14, 30, 0, TimeSpan.FromHours(2));

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                null,
                successfulLoginAt,
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Equal(successfulLoginAt.UtcDateTime, saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(account.StateRevision, saved.StateRevision);
        Assert.Equal(OriginalPasswordHash, saved.PasswordCredential.PasswordHash);
        Assert.Equal(
            account.PasswordCredential.PasswordChangedAtUtc,
            saved.PasswordCredential.PasswordChangedAtUtc
        );
        Assert.Equal(account.PasswordCredential.Revision, saved.PasswordCredential.Revision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_SameInstantCanBeRecordedRepeatedly()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        DateTimeOffset successfulLoginAt = CreateSuccessfulLoginInstant();

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                null,
                successfulLoginAt,
                CancellationToken
            )
        );

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                null,
                successfulLoginAt,
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Equal(successfulLoginAt.UtcDateTime, saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(1UL, saved.StateRevision);
        Assert.Equal(1UL, saved.PasswordCredential.Revision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_OlderConcurrentCompletionCannotMoveLoginInstantBackward()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        DateTimeOffset newer = new(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);

        DateTimeOffset older = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(snapshot, null, newer, CancellationToken)
        );

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(snapshot, null, older, CancellationToken)
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Equal(newer.UtcDateTime, saved.LastSuccessfulLoginAtUtc);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_ReplacementUpdatesCredentialAtomically()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        DateTimeOffset successfulLoginAt = CreateSuccessfulLoginInstant();

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                ReplacementPasswordHash,
                successfulLoginAt,
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Equal(successfulLoginAt.UtcDateTime, saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(account.StateRevision, saved.StateRevision);
        Assert.Equal(ReplacementPasswordHash, saved.PasswordCredential.PasswordHash);
        Assert.Equal(successfulLoginAt.UtcDateTime, saved.PasswordCredential.PasswordChangedAtUtc);
        Assert.Equal(account.PasswordCredential.Revision + 1, saved.PasswordCredential.Revision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_StaleCredentialSnapshotIsRejected()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        Assert.True(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                ReplacementPasswordHash,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                null,
                CreateSuccessfulLoginInstant().AddMinutes(1),
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Equal(ReplacementPasswordHash, saved.PasswordCredential.PasswordHash);
        Assert.Equal(2UL, saved.PasswordCredential.Revision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_StaleAccountStateIsRejected()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        await SetAccessStatusAsync(account.AccountId, AccountAccessStatus.Suspended);

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                null,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Null(saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(AccountAccessStatus.Suspended, saved.AccessStatus);
        Assert.Equal(2UL, saved.StateRevision);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_DeletedAccountIsRejected()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        await DeleteAccountAsync(account.AccountId);

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                snapshot,
                ReplacementPasswordHash,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.NotNull(saved.DeletedAtUtc);
        Assert.Null(saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(OriginalPasswordHash, saved.PasswordCredential.PasswordHash);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_UsernameComparisonIsByteExact()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot storedSnapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        string differentlyCasedUsername = storedSnapshot.Username.ToUpperInvariant();

        Assert.NotEqual(storedSnapshot.Username, differentlyCasedUsername);

        AccountAuthenticationSnapshot alteredSnapshot = new(
            storedSnapshot.AccountId,
            differentlyCasedUsername,
            storedSnapshot.PasswordHash,
            storedSnapshot.Access,
            storedSnapshot.AccountStateRevision,
            storedSnapshot.PasswordCredentialRevision
        );

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                alteredSnapshot,
                null,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        Assert.Null((await ReadAccountAsync(account.AccountId)).LastSuccessfulLoginAtUtc);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_PasswordHashComparisonIsByteExact()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot storedSnapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        string differentlyCasedHash = storedSnapshot.PasswordHash.ToUpperInvariant();

        Assert.NotEqual(storedSnapshot.PasswordHash, differentlyCasedHash);

        AccountAuthenticationSnapshot alteredSnapshot = new(
            storedSnapshot.AccountId,
            storedSnapshot.Username,
            differentlyCasedHash,
            storedSnapshot.Access,
            storedSnapshot.AccountStateRevision,
            storedSnapshot.PasswordCredentialRevision
        );

        Assert.False(
            await Repository.TryRecordSuccessfulLoginAsync(
                alteredSnapshot,
                ReplacementPasswordHash,
                CreateSuccessfulLoginInstant(),
                CancellationToken
            )
        );

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.Null(saved.LastSuccessfulLoginAtUtc);
        Assert.Equal(OriginalPasswordHash, saved.PasswordCredential.PasswordHash);
    }

    [Fact]
    public async Task TryRecordSuccessfulLoginAsync_ConcurrentReplacementHasExactlyOneWinner()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        DateTimeOffset successfulLoginAt = CreateSuccessfulLoginInstant();

        Task<bool>[] operations = Enumerable
            .Range(0, 8)
            .Select(index =>
                Repository
                    .TryRecordSuccessfulLoginAsync(
                        snapshot,
                        ReplacementPasswordHash + index,
                        successfulLoginAt,
                        CancellationToken
                    )
                    .AsTask()
            )
            .ToArray();

        bool[] results = await Task.WhenAll(operations);

        Assert.Single(results, succeeded => succeeded);

        AccountRecord saved = await ReadAccountAsync(account.AccountId);

        Assert.StartsWith(ReplacementPasswordHash, saved.PasswordCredential.PasswordHash);
        Assert.Equal(2UL, saved.PasswordCredential.Revision);
        Assert.Equal(successfulLoginAt.UtcDateTime, saved.LastSuccessfulLoginAtUtc);
    }

    [Fact]
    public async Task Operations_ObserveCallerCancellation()
    {
        AccountRecord account = await InsertAccountAsync();

        AccountAuthenticationSnapshot snapshot = Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Repository.FindByNameAsync(account.Username, cancellation.Token).AsTask()
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Repository
                .TryRecordSuccessfulLoginAsync(
                    snapshot,
                    null,
                    CreateSuccessfulLoginInstant(),
                    cancellation.Token
                )
                .AsTask()
        );
    }

    private async Task<AccountAuthenticationSnapshot> InsertAndFindAllowedSnapshotAsync()
    {
        AccountRecord account = await InsertAccountAsync();

        return Assert.IsType<AccountAuthenticationSnapshot>(
            await Repository.FindByNameAsync(account.Username, CancellationToken)
        );
    }

    private async Task<AccountRecord> InsertAccountAsync(
        string passwordHash = OriginalPasswordHash,
        AccountAccessStatus accessStatus = AccountAccessStatus.Active
    )
    {
        string username = CreateUsername();

        AccountRecord account = new()
        {
            Username = username,
            AccessStatus = accessStatus,
            AuthorityRole = AccountAuthorityRole.Player,
            CreationOperationId = Guid.NewGuid(),
            LastSuccessfulLoginAtUtc = null,
            StateRevision = 1,
            CreatedAtUtc = s_createdAtUtc,
            CreatedByActorKind = AccountActorKind.System,
            CreatedByAccountId = null,
            StateChangedAtUtc = s_createdAtUtc,
            StateChangedByActorKind = AccountActorKind.System,
            StateChangedByAccountId = null,
            DeletedAtUtc = null,
            DeletedByActorKind = null,
            DeletedByAccountId = null,
        };

        AccountPasswordCredentialRecord credential = new()
        {
            PasswordHash = passwordHash,
            PasswordChangedAtUtc = s_createdAtUtc,
            Revision = 1,
            Account = account,
        };

        account.PasswordCredential = credential;

        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        db.Accounts.Add(account);

        await db.SaveChangesAsync(CancellationToken);

        return account;
    }

    private async Task<AccountRecord> ReadAccountAsync(uint accountId)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        return await db
            .Accounts.AsNoTracking()
            .Include(account => account.PasswordCredential)
            .SingleAsync(account => account.AccountId == accountId, CancellationToken);
    }

    private async Task SetAccessStatusAsync(uint accountId, AccountAccessStatus accessStatus)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        AccountRecord account = await db.Accounts.SingleAsync(
            candidate => candidate.AccountId == accountId,
            CancellationToken
        );

        account.AccessStatus = accessStatus;
        account.StateRevision++;
        account.StateChangedAtUtc = s_createdAtUtc.AddMinutes(1);
        account.StateChangedByActorKind = AccountActorKind.System;
        account.StateChangedByAccountId = null;

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task DeleteAccountAsync(uint accountId)
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        AccountRecord account = await db.Accounts.SingleAsync(
            candidate => candidate.AccountId == accountId,
            CancellationToken
        );

        DateTime deletedAtUtc = s_createdAtUtc.AddMinutes(1);

        account.StateRevision++;
        account.StateChangedAtUtc = deletedAtUtc;
        account.StateChangedByActorKind = AccountActorKind.System;
        account.StateChangedByAccountId = null;
        account.DeletedAtUtc = deletedAtUtc;
        account.DeletedByActorKind = AccountActorKind.System;
        account.DeletedByAccountId = null;

        await db.SaveChangesAsync(CancellationToken);
    }

    private static DateTimeOffset CreateSuccessfulLoginInstant()
    {
        return new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    }

    private static string CreateUsername()
    {
        return "User" + Guid.NewGuid().ToString("N")[..28];
    }
}
