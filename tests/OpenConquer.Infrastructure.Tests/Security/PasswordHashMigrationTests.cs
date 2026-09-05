using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Security;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class PasswordHashMigrationTests
{
    private const string Prefix = "$openconquer$identity-v3$";

    [Theory]
    [InlineData("Test1234")]
    [InlineData(" pässwörd-密碼 ")]
    public async Task AuthenticateAsync_MigratesActualOpenConquerPublicIdentityHash(string password)
    {
        string identityV3Hash = CreateIdentityV3Hash(password);
        MigrationRepository repository = new(identityV3Hash);
        AccountPasswordHasher verifier = new();
        AccountAuthenticator authenticator = new(repository, verifier, new AttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync("  Bernie  ", password.AsMemory(),
            IPAddress.Loopback, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(42u, result.AccountId);
        Assert.Equal("Bernie", repository.LastName);
        Assert.Equal(1, repository.Replacements);
        Assert.Equal("Bernie", result.Username);
        Assert.Equal(identityV3Hash, repository.ExpectedHash);
        Assert.StartsWith("$openconquer$pbkdf2-sha256$v=1$", repository.Hash);
        Assert.Equal(AccountPasswordVerificationStatus.Success, verifier.VerifyPassword(repository.Hash, password));

        Assert.True((await authenticator.AuthenticateAsync("Bernie", password.AsMemory(), IPAddress.Loopback,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, repository.Replacements);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongIdentityV3PasswordNeverMigrates()
    {
        string hash = CreateIdentityV3Hash("Test1234");
        MigrationRepository repository = new(hash);
        AccountAuthenticator authenticator = new(repository, new AccountPasswordHasher(), new AttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync("Bernie", "wrong".AsMemory(),
            IPAddress.Loopback, TestContext.Current.CancellationToken);

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, result.Status);
        Assert.Equal(hash, repository.Hash);
        Assert.Equal(0, repository.Replacements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_RehashConflictVerifiesTheActualReplacementPassword(bool passwordReset)
    {
        AccountPasswordHasher verifier = new();
        string concurrentHash = verifier.HashPassword(passwordReset ? "reset-password" : "Test1234");
        MigrationRepository repository = new(CreateIdentityV3Hash("Test1234")) { ConflictHash = concurrentHash };
        AccountAuthenticator authenticator = new(repository, verifier, new AttemptLimiter(), TimeProvider.System);

        AccountAuthenticationResult result = await authenticator.AuthenticateAsync("Bernie", "Test1234".AsMemory(),
            IPAddress.Loopback, TestContext.Current.CancellationToken);

        Assert.Equal(passwordReset ? AccountAuthenticationStatus.InvalidCredentials : AccountAuthenticationStatus.Success, result.Status);
        Assert.Equal(concurrentHash, repository.Hash);
        Assert.Equal(0, repository.Replacements);
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 0u)]
    [InlineData(1, 1u)]
    [InlineData(1, uint.MaxValue)]
    [InlineData(5, 0u)]
    [InlineData(5, 219999u)]
    [InlineData(5, 220001u)]
    [InlineData(5, uint.MaxValue)]
    [InlineData(9, 0u)]
    [InlineData(9, 15u)]
    [InlineData(9, uint.MaxValue)]
    public void VerifyPassword_RejectsAlteredIdentityParameters(int offset, uint value)
    {
        byte[] bytes = Convert.FromBase64String(CreateIdentityV3Hash("Test1234")[Prefix.Length..]);
        if (offset == 0)
        {
            bytes[0] = (byte)value;
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        }

        AccountPasswordHasher verifier = new();
        Assert.Equal(AccountPasswordVerificationStatus.Failed,
            verifier.VerifyPassword(Prefix + Convert.ToBase64String(bytes), "Test1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AAAA")]
    [InlineData("AQAAAAIA")]
    public void VerifyPassword_MalformedIdentityFailsClosed(string encoded)
    {
        Assert.Equal(AccountPasswordVerificationStatus.Failed,
            new AccountPasswordHasher().VerifyPassword(Prefix + encoded, "Test1234"));
    }

    [Fact]
    public void VerifyPassword_TruncatedExtendedAndUnprefixedIdentityFailClosed()
    {
        string identityV3 = CreateIdentityV3Hash("Test1234");
        AccountPasswordHasher verifier = new();
        foreach (string invalid in new[] { identityV3[..^4], identityV3 + "AAAA", identityV3[Prefix.Length..], identityV3 + "$extra" })
        {
            Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword(invalid, "Test1234"));
        }
    }

    internal static string CreateIdentityV3Hash(string password)
    {
        // The same framework hasher and options used by OpenConquerPublic, independent of our parser.
        PasswordHasher<object> identity = new(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 220_000,
        }));
        return Prefix + identity.HashPassword(new object(), password);
    }

    private sealed class MigrationRepository(string hash) : IAccountAuthenticationRepository
    {
        public string Hash { get; private set; } = hash;
        public string? ConflictHash { get; init; }
        public string? LastName { get; private set; }
        public string? ExpectedHash { get; private set; }
        public int Replacements { get; private set; }

        public ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastName = accountName;
            return ValueTask.FromResult<AccountAuthenticationSnapshot?>(new(42, "Bernie", Hash, AccountLoginAccess.Allowed));
        }

        public ValueTask<bool> TryRecordLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash,
            uint loginTimestamp, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(42u, account.AccountId);
            ExpectedHash = account.PasswordHash;
            if (ConflictHash is not null && Hash != ConflictHash)
            {
                Hash = ConflictHash;
                return ValueTask.FromResult(false);
            }
            if (!string.Equals(Hash, account.PasswordHash, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            if (replacementPasswordHash is not null)
            {
                Hash = replacementPasswordHash;
                Replacements++;
            }
            return ValueTask.FromResult(true);
        }
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
}
