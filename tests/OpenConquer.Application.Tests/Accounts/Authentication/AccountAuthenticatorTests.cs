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
    public void Constructor_RejectsNullRepository()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(null!, new FakePasswordVerifier(), new FakeAttemptLimiter())
        );
    }

    [Fact]
    public void Constructor_RejectsNullPasswordVerifier()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(new FakeRepository(), null!, new FakeAttemptLimiter())
        );
    }

    [Fact]
    public void Constructor_RejectsNullAttemptLimiter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticator(new FakeRepository(), new FakePasswordVerifier(), null!)
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

        FakePasswordVerifier passwordVerifier = new() { ExpectedPassword = Password };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new() { ExpectedPassword = Password };

        FakeAttemptLimiter attemptLimiter = new() { Admit = false };

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Failed,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Failed,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.Success,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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
            PasswordHashReplacementResult = true,
        };

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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
    public async Task AuthenticateAsync_RehashReplacementRaceDoesNotRejectSuccessfulAuthentication()
    {
        FakeRepository repository = new()
        {
            Snapshot = CreateSnapshot(AccountLoginAccess.Allowed),
            PasswordHashReplacementResult = false,
        };

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
        };

        AccountAuthenticator authenticator = new(
            repository,
            passwordVerifier,
            new FakeAttemptLimiter()
        );

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync(
            AccountName,
            Password.AsMemory(),
            s_remoteAddress,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.AccountId);

        Assert.Equal(1, repository.PasswordHashReplacementCount);
    }

    [Fact]
    public async Task AuthenticateAsync_BannedAccountDoesNotRehashPassword()
    {
        FakeRepository repository = new() { Snapshot = CreateSnapshot(AccountLoginAccess.Banned) };

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
        };

        AccountAuthenticator authenticator = new(
            repository,
            passwordVerifier,
            new FakeAttemptLimiter()
        );

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

        FakePasswordVerifier passwordVerifier = new();

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            OnVerifyDecoy = cancellation.Cancel,
        };

        AccountAuthenticator authenticator = new(
            new FakeRepository(),
            passwordVerifier,
            new FakeAttemptLimiter()
        );

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            OnVerifyPassword = cancellation.Cancel,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

        FakePasswordVerifier passwordVerifier = new()
        {
            ExpectedPassword = Password,
            VerificationStatus = AccountPasswordVerificationStatus.SuccessRehashNeeded,
            ReplacementPasswordHash = ReplacementPasswordHash,
            OnHashPassword = cancellation.Cancel,
        };

        FakeAttemptLimiter attemptLimiter = new();

        AccountAuthenticator authenticator = new(repository, passwordVerifier, attemptLimiter);

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

    private static AccountAuthenticator CreateAuthenticator()
    {
        return new AccountAuthenticator(
            new FakeRepository(),
            new FakePasswordVerifier(),
            new FakeAttemptLimiter()
        );
    }

    private static AccountAuthenticationSnapshot CreateSnapshot(AccountLoginAccess access)
    {
        return new AccountAuthenticationSnapshot(AccountId, CurrentPasswordHash, access);
    }

    private sealed class FakeRepository : IAccountAuthenticationRepository
    {
        public AccountAuthenticationSnapshot? Snapshot { get; set; }

        public bool PasswordHashReplacementResult { get; set; } = true;

        public int FindCount { get; private set; }

        public string? LastAccountName { get; private set; }

        public int PasswordHashReplacementCount { get; private set; }

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

        public ValueTask<bool> TryReplacePasswordHashAsync(
            uint accountId,
            string expectedPasswordHash,
            string replacementPasswordHash,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            PasswordHashReplacementCount++;

            LastReplacementAccountId = accountId;

            LastExpectedPasswordHash = expectedPasswordHash;

            LastReplacementPasswordHash = replacementPasswordHash;

            return ValueTask.FromResult(PasswordHashReplacementResult);
        }
    }

    private sealed class FakePasswordVerifier : IAccountPasswordVerifier
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
