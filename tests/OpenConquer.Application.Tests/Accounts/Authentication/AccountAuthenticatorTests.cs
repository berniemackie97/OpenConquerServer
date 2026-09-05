using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class AccountAuthenticatorTests
{
    private const uint AccountId = 42;

    private const string AccountName = "bernie";

    private const string Password = "Test1234";

    private const string CurrentPasswordHash = "$openconquer$current$";

    private const string ReplacementPasswordHash = "$openconquer$replacement$";

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.10");

    [Fact]
    public void Constructor_RejectsNullTimeProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountAuthenticator(
            new FakeRepository(), new FakePasswordHasher(), new FakeAttemptLimiter(), null!));
    }

    [Fact]
    public async Task AuthenticateAsync_PersistsTimestampAndReturnsCanonicalUsername()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        AccountAuthenticator authenticator = new(repository, new FakePasswordHasher(), new FakeAttemptLimiter(),
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_788_566_400)));

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync("  BERNIE  ", Password.AsMemory(),
            s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bernie", result.Username);
        Assert.Equal(1, repository.LoginRecordCount);
        Assert.Equal(1_788_566_400u, repository.LastLoginTimestamp);
        Assert.Null(repository.LastReplacementPasswordHash);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(4_294_967_296L)]
    public async Task AuthenticateAsync_UnrepresentableTimestampAbandonsAttempt(long timestamp)
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, new FakePasswordHasher(), limiter,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(timestamp)));

        await Assert.ThrowsAsync<OverflowException>(() => authenticator.AuthenticateAsync(AccountName, Password.AsMemory(),
            s_remoteAddress, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.LoginRecordCount);
        Assert.False(limiter.LastAttempt!.IsCompleted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_CurrentHashPersistenceFailureAbandonsAttempt(bool cancel)
    {
        using CancellationTokenSource cancellation = new();
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        repository.OnRecordLogin = () =>
        {
            if (cancel)
            {
                cancellation.Cancel();
            }
            else
            {
                throw new IOException("Database write failed.");
            }
        };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, new FakePasswordHasher(), limiter, TimeProvider.System);

        Task<AccountAuthenticationResult> operation = authenticator.AuthenticateAsync(AccountName, Password.AsMemory(),
            s_remoteAddress, cancellation.Token).AsTask();
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => operation);
        }

        Assert.False(limiter.LastAttempt!.IsCompleted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(null!, new FakePasswordHasher(), new FakeAttemptLimiter(), TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_RejectsNullPasswordVerifier()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(new FakeRepository(), null!, new FakeAttemptLimiter(), TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_RejectsNullAttemptLimiter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(new FakeRepository(), new FakePasswordHasher(), null!, TimeProvider.System)
        );
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsNullAccountName()
    {
        AccountAuthenticator authenticator = CreateAuthenticator();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            authenticator
                .AuthenticateAsync(
                    null!,
                    Password.AsMemory(),
                    s_remoteAddress,
                    TestContext.Current.CancellationToken
                )
                .AsTask()
        );
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsNullRemoteAddress()
    {
        AccountAuthenticator authenticator = CreateAuthenticator();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            authenticator
                .AuthenticateAsync(
                    AccountName,
                    Password.AsMemory(),
                    null!,
                    TestContext.Current.CancellationToken
                )
                .AsTask()
        );
    }

    [Fact]
    public async Task AuthenticateAsync_AccountNotFoundUsesDecoyAndReturnsInvalidCredentials()
    {
        FakeRepository repository = new();

        FakePasswordHasher passwordVerifier = new() { ExpectedPassword = Password };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);

        Assert.False(result.IsSuccess);
        Assert.Equal(0u, result.AccountId);

        Assert.Equal(1, repository.FindCount);
        Assert.Equal(AccountName, repository.LastAccountName);

        Assert.Equal(1, passwordVerifier.DecoyVerificationCount);

        Assert.True(passwordVerifier.DecoyPasswordMatched);

        Assert.Equal(0, passwordVerifier.PasswordVerificationCount);

        Assert.Equal(0, attemptLimiter.BeginCount);

        Assert.Equal(0, passwordVerifier.HashCount);

        Assert.Equal(0, repository.PasswordHashReplacementCount);
    }

    [Fact]
    public async Task AuthenticateAsync_ProtectionRejectionReturnsInvalidCredentialsWithoutHashing()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };

        FakePasswordHasher passwordVerifier = new() { ExpectedPassword = Password };

        FakeAttemptLimiter attemptLimiter = new() { Admit = false };

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);

        Assert.Equal(1, attemptLimiter.BeginCount);

        Assert.Equal(s_remoteAddress, attemptLimiter.LastRemoteAddress);

        Assert.Equal(AccountId, attemptLimiter.LastAccountId);

        Assert.Equal(0, passwordVerifier.PasswordVerificationCount);

        Assert.Equal(0, passwordVerifier.DecoyVerificationCount);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidPasswordReturnsInvalidCredentials()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Failed,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);

        Assert.Equal(1, passwordVerifier.PasswordVerificationCount);

        Assert.True(passwordVerifier.VerifiedPasswordMatched);

        Assert.Equal(0, passwordVerifier.DecoyVerificationCount);

        Assert.Equal(0, repository.PasswordHashReplacementCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.False(attempt.CredentialsAccepted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidPasswordDoesNotRevealBannedAccount()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Banned) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Failed,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.False(attempt.CredentialsAccepted);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidPasswordForBannedAccountReturnsBanned()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Banned) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.Banned, result.Status);

        Assert.False(result.IsSuccess);
        Assert.Equal(0u, result.AccountId);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.True(attempt.CredentialsAccepted);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidPasswordForDeniedAccountReturnsInvalidCredentials()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Denied) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.True(attempt.CredentialsAccepted);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidPasswordForAllowedAccountReturnsAccountId()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.Success, result.Status);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.AccountId);

        Assert.Equal(0, passwordVerifier.HashCount);

        Assert.Equal(0, repository.PasswordHashReplacementCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.True(attempt.CredentialsAccepted);
    }

    [Fact]
    public async Task AuthenticateAsync_RehashNeededAttemptsOptimisticReplacement()
    {
        FakeRepository repository = new()
        {
            Snapshot = CreateSnapshot(AccountLoginAccess.Allowed),
            RecordLoginResult = true,
        };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.AccountId);

        Assert.Equal(1, passwordVerifier.HashCount);

        Assert.True(passwordVerifier.HashedPasswordMatched);

        Assert.Equal(1, repository.PasswordHashReplacementCount);

        Assert.Equal(AccountId, repository.LastReplacementAccountId);

        Assert.Equal(CurrentPasswordHash, repository.LastExpectedPasswordHash);

        Assert.Equal(ReplacementPasswordHash, repository.LastReplacementPasswordHash);
    }

    [Fact]
    public async Task AuthenticateAsync_RepeatedPersistenceConflictsRejectAuthentication()
    {
        FakeRepository repository = new()
        {
            Snapshot = CreateSnapshot(AccountLoginAccess.Allowed),
            RecordLoginResult = false,
        };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
        };

        AccountAuthenticator authenticator = new(
            repository,
            passwordVerifier,
            new FakeAttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(0u, result.AccountId);
        Assert.Equal(2, repository.PasswordHashReplacementCount);
        Assert.Equal(2, repository.FindCount);
    }

    [Fact]
    public async Task AuthenticateAsync_BannedAccountDoesNotRehashPassword()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Banned) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
        };

        AccountAuthenticator authenticator = new(
            repository,
            passwordVerifier,
            new FakeAttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AccountAuthenticationStatus.Banned, result.Status);

        Assert.Equal(0, passwordVerifier.HashCount);

        Assert.Equal(0, repository.PasswordHashReplacementCount);
    }

    [Fact]
    public async Task AuthenticateAsync_PropagatesCallerCancellation()
    {
        FakeRepository repository = new();

        FakePasswordHasher passwordVerifier = new();

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            authenticator
                .AuthenticateAsync(
                    AccountName,
                    Password.AsMemory(),
                    s_remoteAddress,
                    cancellation.Token
                )
                .AsTask()
        );

        Assert.Equal(0, passwordVerifier.PasswordVerificationCount);

        Assert.Equal(0, passwordVerifier.DecoyVerificationCount);

        Assert.Equal(0, attemptLimiter.BeginCount);
    }

    [Fact]
    public async Task AuthenticateAsync_CancellationDuringDecoyVerificationIsObserved()
    {
        using CancellationTokenSource cancellation = new();

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            OnVerifyDecoy = cancellation.Cancel,
        };

        AccountAuthenticator authenticator = new(
            new FakeRepository(),
            passwordVerifier,
            new FakeAttemptLimiter(), TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            authenticator
                .AuthenticateAsync(
                    AccountName,
                    Password.AsMemory(),
                    s_remoteAddress,
                    cancellation.Token
                )
                .AsTask()
        );

        Assert.Equal(1, passwordVerifier.DecoyVerificationCount);
    }

    [Fact]
    public async Task AuthenticateAsync_CancellationDuringPasswordVerificationAbandonsAttempt()
    {
        using CancellationTokenSource cancellation = new();

        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            OnVerifyPassword = cancellation.Cancel,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            authenticator
                .AuthenticateAsync(
                    AccountName,
                    Password.AsMemory(),
                    s_remoteAddress,
                    cancellation.Token
                )
                .AsTask()
        );

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.False(attempt.IsCompleted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task AuthenticateAsync_CancellationDuringPasswordRehashAbandonsAttempt()
    {
        using CancellationTokenSource cancellation = new();

        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };

        FakePasswordHasher passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
            OnHashPassword = cancellation.Cancel,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            authenticator
                .AuthenticateAsync(
                    AccountName,
                    Password.AsMemory(),
                    s_remoteAddress,
                    cancellation.Token
                )
                .AsTask()
        );

        Assert.Equal(0, repository.PasswordHashReplacementCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(attemptLimiter.LastAttempt);

        Assert.False(attempt.IsCompleted);
        Assert.True(attempt.IsDisposed);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData(" \t\r\n", "password")]
    [InlineData("bernie", "")]
    [InlineData("123456789012345678901234567890123", "password")]
    public async Task AuthenticateAsync_InvalidCredentialsNeverReachDependencies(string accountName, string password)
    {
        await AssertRejectedBeforeDependencies(accountName, password.AsMemory());
    }

    [Fact]
    public async Task AuthenticateAsync_OversizedPasswordNeverReachesDependencies()
    {
        await AssertRejectedBeforeDependencies(AccountName, new string('x', 129).AsMemory());
    }

    [Fact]
    public async Task AuthenticateAsync_DefaultPasswordMemoryNeverReachesDependencies()
    {
        await AssertRejectedBeforeDependencies(AccountName, default);
    }

    private static async Task AssertRejectedBeforeDependencies(string accountName, ReadOnlyMemory<char> password)
    {
        FakeRepository repository = new();
        FakePasswordHasher verifier = new();
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, verifier, limiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(accountName, password,
            s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);
        Assert.Equal(0u, result.AccountId);
        Assert.Equal(0, repository.FindCount);
        Assert.Equal(0, repository.PasswordHashReplacementCount);
        Assert.Equal(0, verifier.PasswordVerificationCount);
        Assert.Equal(0, verifier.DecoyVerificationCount);
        Assert.Equal(0, verifier.HashCount);
        Assert.Equal(0, limiter.BeginCount);
    }

    [Theory]
    [InlineData("  Bernie  ", "Bernie", " password ")]
    [InlineData("\tBérnie 名\u2003", "Bérnie 名", " ")]
    [InlineData("  12345678901234567890123456789012  ", "12345678901234567890123456789012", "password")]
    public async Task AuthenticateAsync_NormalizesNameOnly(string supplied, string expected, string password)
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        FakePasswordHasher verifier = new() { ExpectedPassword = password };
        AccountAuthenticator authenticator = new(repository, verifier, new FakeAttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(supplied, password.AsMemory(),
            s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, repository.LastAccountName);
        Assert.True(verifier.VerifiedPasswordMatched);
    }

    [Fact]
    public async Task AuthenticateAsync_MaximumPasswordIsPassedVerbatim()
    {
        string password = new('x', 128);
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        FakePasswordHasher verifier = new() { ExpectedPassword = password };
        AccountAuthenticator authenticator = new(repository, verifier, new FakeAttemptLimiter(), TimeProvider.System);

        Assert.True((await authenticator.AuthenticateAsync(AccountName, password.AsMemory(), s_remoteAddress,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True(verifier.VerifiedPasswordMatched);
    }

    [Theory]
    [InlineData(AccountLoginAccess.Allowed, AccountPasswordVerificationStatus.Failed, AccountAuthenticationStatus.InvalidCredentials, false)]
    [InlineData(AccountLoginAccess.Banned, AccountPasswordVerificationStatus.Success, AccountAuthenticationStatus.Banned, true)]
    [InlineData(AccountLoginAccess.Denied, AccountPasswordVerificationStatus.Success, AccountAuthenticationStatus.InvalidCredentials, true)]
    [InlineData(AccountLoginAccess.Allowed, AccountPasswordVerificationStatus.Success, AccountAuthenticationStatus.Success, true)]
    public async Task AuthenticateAsync_RehashConflictRevalidatesCredentialsAndAccess(AccountLoginAccess access,
        AccountPasswordVerificationStatus refreshedStatus, AccountAuthenticationStatus expected, bool credentialsAccepted)
    {
        FakePasswordHasher verifier = new() { VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded };
        FakeRepository repository = new()
        {
            Snapshot = CreateSnapshot(AccountLoginAccess.Allowed),
            RecordLoginResult = false,
        };
        repository.OnRecordLogin = () =>
        {
            repository.Snapshot = CreateSnapshot(access);
            verifier.VerificationStatus = refreshedStatus;
            repository.RecordLoginResult = repository.LoginRecordCount > 1;
        };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, verifier, limiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(AccountName, Password.AsMemory(),
            s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Equal(2, repository.FindCount);
        Assert.Equal(2, verifier.PasswordVerificationCount);
        Assert.Equal(1, repository.PasswordHashReplacementCount);
        Assert.Equal(1, verifier.HashCount);
        Assert.Equal(1, limiter.BeginCount);
        Assert.True(limiter.LastAttempt!.IsCompleted);
        Assert.Equal(credentialsAccepted, limiter.LastAttempt.CredentialsAccepted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_RehashConflictRejectsDeletedOrRecreatedAccount(bool recreated)
    {
        FakeRepository repository = new()
        {
            Snapshot = CreateSnapshot(AccountLoginAccess.Allowed),
            RecordLoginResult = false,
        };
        repository.OnRecordLogin = () => repository.Snapshot = recreated
            ? new AccountAuthenticationSnapshot(AccountId + 1, "Bernie", CurrentPasswordHash, AccountLoginAccess.Allowed)
            : null;
        FakePasswordHasher verifier = new() { VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, verifier, limiter, TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(AccountName, Password.AsMemory(),
            s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);
        Assert.Equal(1, verifier.PasswordVerificationCount);
        Assert.Equal(1, verifier.DecoyVerificationCount);
        Assert.False(limiter.LastAttempt!.CredentialsAccepted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_ReplacementFailureAbandonsAttempt(bool cancellationRequested)
    {
        using CancellationTokenSource cancellation = new();
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        repository.OnRecordLogin = () =>
        {
            if (cancellationRequested)
            {
                cancellation.Cancel();
            }
            else
            {
                throw new IOException("Persistence failed.");
            }
        };
        FakePasswordHasher verifier = new() { VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, verifier, limiter, TimeProvider.System);

        Task<AccountAuthenticationResult> operation = authenticator.AuthenticateAsync(AccountName, Password.AsMemory(),
            s_remoteAddress, cancellation.Token).AsTask();
        if (cancellationRequested)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => operation);
        }

        Assert.False(limiter.LastAttempt!.IsCompleted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    [Fact]
    public async Task AuthenticateAsync_UnsupportedVerifierStatusAbandonsAttempt()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Allowed) };
        FakePasswordHasher verifier = new() { VerificationStatus = (AccountPasswordVerificationStatus)99 };
        FakeAttemptLimiter limiter = new();
        AccountAuthenticator authenticator = new(repository, verifier, limiter, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => authenticator.AuthenticateAsync(AccountName,
            Password.AsMemory(), s_remoteAddress, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, repository.PasswordHashReplacementCount);
        Assert.False(limiter.LastAttempt!.IsCompleted);
        Assert.True(limiter.LastAttempt.IsDisposed);
    }

    private static AccountAuthenticator CreateAuthenticator()
    {
        return new AccountAuthenticator(
            new FakeRepository(),
            new FakePasswordHasher(),
            new FakeAttemptLimiter(), TimeProvider.System);
    }

    private static AccountAuthenticationSnapshot CreateSnapshot(AccountLoginAccess access)
    {
        return new AccountAuthenticationSnapshot(AccountId, "Bernie", CurrentPasswordHash, access);
    }

    private sealed class FakeRepository : IAccountAuthenticationRepository
    {
        public AccountAuthenticationSnapshot? Snapshot { get; set; }

        public bool RecordLoginResult { get; set; } = true;

        public Action? OnRecordLogin { get; set; }

        public int FindCount { get; private set; }

        public string? LastAccountName { get; private set; }

        public int PasswordHashReplacementCount { get; private set; }

        public int LoginRecordCount { get; private set; }

        public uint LastLoginTimestamp { get; private set; }

        public uint LastReplacementAccountId { get; private set; }

        public string? LastExpectedPasswordHash { get; private set; }

        public string? LastReplacementPasswordHash { get; private set; }

        public ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(
            string accountName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            FindCount++;
            LastAccountName = accountName;

            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<bool> TryRecordLoginAsync(
            AccountAuthenticationSnapshot account,
            string? replacementPasswordHash,
            uint loginTimestamp,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            LoginRecordCount++;
            LastLoginTimestamp = loginTimestamp;
            if (replacementPasswordHash is not null)
            {
                PasswordHashReplacementCount++;
            }

            LastReplacementAccountId = account.AccountId;

            LastExpectedPasswordHash = account.PasswordHash;

            LastReplacementPasswordHash = replacementPasswordHash;

            OnRecordLogin?.Invoke();
            return ValueTask.FromResult(RecordLoginResult);
        }
    }

    private sealed class FakePasswordHasher : IAccountPasswordHasher
    {
        public string ExpectedPassword { get; set; } = Password;

        public AccountPasswordVerificationStatus VerificationStatus { get; set; } =
            AccountPasswordVerificationStatus.Success;

        public string ReplacementPasswordHash { get; set; } = "$replacement$";

        public Action? OnVerifyPassword { get; set; }

        public Action? OnVerifyDecoy { get; set; }

        public Action? OnHashPassword { get; set; }

        public int PasswordVerificationCount { get; private set; }

        public int DecoyVerificationCount { get; private set; }

        public int HashCount { get; private set; }

        public bool VerifiedPasswordMatched { get; private set; }

        public bool DecoyPasswordMatched { get; private set; }

        public bool HashedPasswordMatched { get; private set; }

        public AccountPasswordVerificationStatus VerifyPassword(
            string passwordHash,
            ReadOnlySpan<char> password
        )
        {
            Assert.Equal(CurrentPasswordHash, passwordHash);

            PasswordVerificationCount++;

            VerifiedPasswordMatched = password.SequenceEqual(ExpectedPassword.AsSpan());

            OnVerifyPassword?.Invoke();

            return VerificationStatus;
        }

        public void VerifyDecoy(ReadOnlySpan<char> password)
        {
            DecoyVerificationCount++;

            DecoyPasswordMatched = password.SequenceEqual(ExpectedPassword.AsSpan());

            OnVerifyDecoy?.Invoke();
        }

        public string HashPassword(ReadOnlySpan<char> password)
        {
            HashCount++;

            HashedPasswordMatched = password.SequenceEqual(ExpectedPassword.AsSpan());

            OnHashPassword?.Invoke();

            return ReplacementPasswordHash;
        }
    }

    private sealed class FakeAttemptLimiter : IAccountAuthenticationAttemptLimiter
    {
        public bool Admit { get; set; } = true;

        public int BeginCount { get; private set; }

        public IPAddress? LastRemoteAddress { get; private set; }

        public uint LastAccountId { get; private set; }

        public FakeAttemptLease? LastAttempt { get; private set; }

        public bool TryBeginAuthentication(
            IPAddress remoteAddress,
            uint accountId,
            [NotNullWhen(true)] out IAccountAuthenticationAttemptLease? attempt
        )
        {
            BeginCount++;

            LastRemoteAddress = remoteAddress;

            LastAccountId = accountId;

            if (!Admit)
            {
                attempt = null;

                return false;
            }

            FakeAttemptLease concreteAttempt = new();

            LastAttempt = concreteAttempt;

            attempt = concreteAttempt;

            return true;
        }
    }

    private sealed class FakeAttemptLease : IAccountAuthenticationAttemptLease
    {
        public bool IsCompleted { get; private set; }

        public bool CredentialsAccepted { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Complete(bool credentialsAccepted)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FakeAttemptLease));
            }

            if (IsCompleted)
            {
                throw new InvalidOperationException(
                    "The authentication attempt has already been completed."
                );
            }

            IsCompleted = true;
            CredentialsAccepted = credentialsAccepted;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
