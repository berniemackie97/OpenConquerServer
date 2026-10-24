using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.Authentication.Passwords;
using OpenConquer.Application.Accounts.Registration;

namespace OpenConquer.Application.Tests.Accounts.Registration;

public sealed class AccountRegistrationServiceTests
{
    private static readonly IPAddress s_source = IPAddress.Parse("203.0.113.7");

    [Fact]
    public async Task RegisterAsync_HashesPasswordAndCreatesAccount()
    {
        FakeStore store = new(createdAccountId: 7);
        FakeHasher hasher = new();
        FakeLimiter limiter = new();
        AccountRegistrationService service = new(store, hasher, limiter);

        AccountRegistrationResult result = await service.RegisterAsync("newplayer", "secret1".AsMemory(), s_source,
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountRegistrationResult.Created(7), result);
        Assert.Equal("secret1", hasher.LastPassword);
        Assert.Equal("newplayer", store.Username);
        Assert.Equal(FakeHasher.Hash, store.PasswordHash);
        Assert.NotEqual(Guid.Empty, store.OperationId);
        Assert.Equal(s_source, limiter.LastSource);
        Assert.Equal(0, limiter.InFlight);
    }

    [Fact]
    public async Task RegisterAsync_ReportsTakenUsernameFromTheStore()
    {
        AccountRegistrationService service = new(new FakeStore(createdAccountId: null), new FakeHasher(), new FakeLimiter());

        AccountRegistrationResult result = await service.RegisterAsync("existing", "secret1".AsMemory(), s_source,
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountRegistrationStatus.UsernameTaken, result.Status);
        Assert.Equal(0u, result.AccountId);
    }

    [Theory]
    [InlineData("abc", "secret1")]
    [InlineData("bad name", "secret1")]
    [InlineData("goodname", "12345")]
    [InlineData("goodname", "1234567890123456")]
    public async Task RegisterAsync_RejectsPolicyViolationsWithoutUsingQuotaOrHashing(string username, string password)
    {
        FakeStore store = new(createdAccountId: 7);
        FakeHasher hasher = new();
        FakeLimiter limiter = new();
        AccountRegistrationService service = new(store, hasher, limiter);

        AccountRegistrationResult result = await service.RegisterAsync(username, password.AsMemory(), s_source,
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountRegistrationStatus.InvalidRequest, result.Status);
        Assert.Equal(0, limiter.Attempts);
        Assert.Null(hasher.LastPassword);
        Assert.Null(store.Username);
    }

    [Fact]
    public async Task RegisterAsync_ThrottledSourceNeverHashesOrWrites()
    {
        FakeStore store = new(createdAccountId: 7);
        FakeHasher hasher = new();
        AccountRegistrationService service = new(store, hasher, new FakeLimiter(admit: false));

        AccountRegistrationResult result = await service.RegisterAsync("newplayer", "secret1".AsMemory(), s_source,
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountRegistrationStatus.Throttled, result.Status);
        Assert.Null(hasher.LastPassword);
        Assert.Null(store.Username);
    }

    [Fact]
    public async Task RegisterAsync_StoreFailureReleasesTheLimiterSlot()
    {
        FakeLimiter limiter = new();
        AccountRegistrationService service = new(new FakeStore(failure: new InvalidOperationException("lost")), new FakeHasher(), limiter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync("newplayer", "secret1".AsMemory(), s_source,
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, limiter.Attempts);
        Assert.Equal(0, limiter.InFlight);
    }

    private sealed class FakeStore(uint? createdAccountId = null, Exception? failure = null) : IAccountRegistrationStore
    {
        public string? Username { get; private set; }
        public string? PasswordHash { get; private set; }
        public Guid OperationId { get; private set; }

        public ValueTask<uint?> TryCreateAsync(string username, string passwordHash, Guid creationOperationId,
            CancellationToken cancellationToken = default)
        {
            Username = username;
            PasswordHash = passwordHash;
            OperationId = creationOperationId;

            return failure is null ? ValueTask.FromResult(createdAccountId) : ValueTask.FromException<uint?>(failure);
        }
    }

    private sealed class FakeHasher : IAccountPasswordHasher
    {
        public const string Hash = "$openconquer$pbkdf2-sha256$v=1$test";

        public string? LastPassword { get; private set; }

        public AccountPasswordVerificationStatus VerifyPassword(string passwordHash, ReadOnlySpan<char> password) =>
            throw new NotSupportedException();

        public void VerifyDecoy(ReadOnlySpan<char> password) => throw new NotSupportedException();

        public string HashPassword(ReadOnlySpan<char> password)
        {
            LastPassword = new string(password);
            return Hash;
        }
    }

    private sealed class FakeLimiter(bool admit = true) : IAccountRegistrationLimiter
    {
        public int Attempts { get; private set; }
        public int InFlight { get; private set; }
        public IPAddress? LastSource { get; private set; }

        public bool TryBeginRegistration(IPAddress remoteAddress, [NotNullWhen(true)] out IDisposable? lease)
        {
            LastSource = remoteAddress;

            if (!admit)
            {
                lease = null;
                return false;
            }

            Attempts++;
            InFlight++;
            lease = new Lease(this);
            return true;
        }

        private sealed class Lease(FakeLimiter owner) : IDisposable
        {
            public void Dispose() => owner.InFlight--;
        }
    }
}
