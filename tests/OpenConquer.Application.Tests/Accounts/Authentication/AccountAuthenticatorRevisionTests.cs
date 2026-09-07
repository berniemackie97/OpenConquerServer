using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class AccountAuthenticatorRevisionTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const string Password = "Test1234";
    private const string PasswordHash = "$openconquer$current$";
    private const string ReplacementPasswordHash = "$openconquer$replacement$";

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.10");

    [Fact]
    public async Task AuthenticateAsync_CurrentHashReturnsAuthenticatedRevisions()
    {
        FakeRepository repository = new(
            CreateSnapshot(accountStateRevision: 7, passwordCredentialRevision: 11)
        );

        AccountAuthenticator authenticator = new(
            repository,
            new FakePasswordHasher(AccountPasswordVerificationStatus.Success),
            new AllowAllAttemptLimiter(),
            TimeProvider.System
        );

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            Username,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal(Username, result.Username);
        Assert.Equal(7ul, result.AccountStateRevision);
        Assert.Equal(11ul, result.PasswordCredentialRevision);
        Assert.Equal(1, repository.SuccessfulLoginRecordCount);
        Assert.Null(repository.LastReplacementPasswordHash);
    }

    [Fact]
    public async Task AuthenticateAsync_RehashReturnsPostPersistenceCredentialRevision()
    {
        FakeRepository repository = new(
            CreateSnapshot(accountStateRevision: 7, passwordCredentialRevision: 11)
        );

        AccountAuthenticator authenticator = new(
            repository,
            new FakePasswordHasher(AccountPasswordVerificationStatus.SuccessRehashNeeded),
            new AllowAllAttemptLimiter(),
            TimeProvider.System
        );

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            Username,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal(7ul, result.AccountStateRevision);
        Assert.Equal(12ul, result.PasswordCredentialRevision);
        Assert.Equal(1, repository.SuccessfulLoginRecordCount);
        Assert.Equal(ReplacementPasswordHash, repository.LastReplacementPasswordHash);
    }

    [Fact]
    public async Task AuthenticateAsync_RehashAtMaximumCredentialRevisionFailsClosedBeforePersistence()
    {
        FakeRepository repository = new(
            CreateSnapshot(accountStateRevision: 7, passwordCredentialRevision: ulong.MaxValue)
        );

        AccountAuthenticator authenticator = new(
            repository,
            new FakePasswordHasher(AccountPasswordVerificationStatus.SuccessRehashNeeded),
            new AllowAllAttemptLimiter(),
            TimeProvider.System
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authenticator
                .AuthenticateAsync(
                    Username,
                    Password.AsMemory(),
                    s_remoteAddress,
                    TestContext.Current.CancellationToken
                )
                .AsTask()
        );

        Assert.Equal(0, repository.SuccessfulLoginRecordCount);
    }

    private static AccountAuthenticationSnapshot CreateSnapshot(
        ulong accountStateRevision,
        ulong passwordCredentialRevision
    )
    {
        return new AccountAuthenticationSnapshot(
            AccountId,
            Username,
            PasswordHash,
            AccountLoginAccess.Allowed,
            accountStateRevision,
            passwordCredentialRevision
        );
    }

    private sealed class FakeRepository(AccountAuthenticationSnapshot snapshot)
        : IAccountAuthenticationRepository
    {
        private readonly AccountAuthenticationSnapshot _snapshot =
            snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        public int SuccessfulLoginRecordCount { get; private set; }

        public string? LastReplacementPasswordHash { get; private set; }

        public ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(
            string accountName,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(accountName);

            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<AccountAuthenticationSnapshot?>(_snapshot);
        }

        public ValueTask<bool> TryRecordSuccessfulLoginAsync(
            AccountAuthenticationSnapshot account,
            string? replacementPasswordHash,
            DateTimeOffset successfulLoginAt,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(account);

            cancellationToken.ThrowIfCancellationRequested();

            SuccessfulLoginRecordCount++;
            LastReplacementPasswordHash = replacementPasswordHash;

            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakePasswordHasher(AccountPasswordVerificationStatus verificationStatus)
        : IAccountPasswordHasher
    {
        private readonly AccountPasswordVerificationStatus _verificationStatus = verificationStatus;

        public string HashPassword(ReadOnlySpan<char> password)
        {
            Assert.True(password.SequenceEqual(Password.AsSpan()));

            return ReplacementPasswordHash;
        }

        public AccountPasswordVerificationStatus VerifyPassword(
            string passwordHash,
            ReadOnlySpan<char> password
        )
        {
            Assert.Equal(PasswordHash, passwordHash);
            Assert.True(password.SequenceEqual(Password.AsSpan()));

            return _verificationStatus;
        }

        public void VerifyDecoy(ReadOnlySpan<char> password)
        {
            throw new InvalidOperationException(
                "Decoy verification is not expected in these tests."
            );
        }
    }

    private sealed class AllowAllAttemptLimiter : IAccountAuthenticationAttemptLimiter
    {
        public bool TryBeginAuthentication(
            IPAddress remoteAddress,
            uint accountId,
            [NotNullWhen(true)] out IAccountAuthenticationAttemptLease? attempt
        )
        {
            ArgumentNullException.ThrowIfNull(remoteAddress);

            if (accountId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accountId));
            }

            attempt = new AttemptLease();

            return true;
        }
    }

    private sealed class AttemptLease : IAccountAuthenticationAttemptLease
    {
        private bool _completed;
        private bool _disposed;

        public void Complete(bool credentialsAccepted)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AttemptLease));
            }

            if (_completed)
            {
                throw new InvalidOperationException(
                    "The authentication attempt has already been completed."
                );
            }

            _completed = true;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
