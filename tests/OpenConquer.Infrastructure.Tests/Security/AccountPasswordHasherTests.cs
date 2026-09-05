using System.Diagnostics;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Security;

namespace OpenConquer.Infrastructure.Tests.Security;

[Collection("Password verification work")]
public sealed class AccountPasswordHasherTests
{
    private const string Password = "Test1234";

    private const string CurrentSchemePrefix = "$openconquer$pbkdf2-sha256$v=1$";

    // PBKDF2-HMAC-SHA256:
    // password = "Test1234"
    // salt     = 000102030405060708090A0B0C0D0E0F
    // rounds   = 600000
    // dkLen    = 32
    private const string KnownV1Hash = "$openconquer$pbkdf2-sha256$v=1$AAECAwQFBgcICQoLDA0ODw==$HPgEfYQxRr+Kw2c55AfeLYbvAto2AZtipPkLZlszeTQ=";

    // Independently generated with Python hashlib.pbkdf2_hmac and the Identity V3 big-endian header.
    private const string KnownIdentityV3Hash = "$openconquer$identity-v3$AQAAAAIAA1tgAAAAEAABAgMEBQYHCAkKCwwNDg8hYcEtILve1j/gJyfmQFXfsV0ZwVBVMOP2zPbYYo2x2A==";

    [Fact]
    public void VerifyPassword_IndependentIdentityVectorRequiresRehash()
    {
        AccountPasswordHasher verifier = new();
        Assert.Equal(AccountPasswordVerificationStatus.SuccessRehashNeeded, verifier.VerifyPassword(KnownIdentityV3Hash, Password));
        Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword(KnownIdentityV3Hash, "wrong"));
    }

    [Fact]
    public async Task SharedVerifierSupportsConcurrentMixedOperations()
    {
        const int count = 8;
        AccountPasswordHasher verifier = new();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>[] tasks = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
        {
            await start.Task;
            char[] password = $" password-{index}-密碼 ".ToCharArray();
            char[] original = password.ToArray();
            string hash = verifier.HashPassword(password);
            Assert.Equal(AccountPasswordVerificationStatus.Success, verifier.VerifyPassword(hash, password));
            Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword(hash, "wrong"));
            Assert.Equal(AccountPasswordVerificationStatus.SuccessRehashNeeded, verifier.VerifyPassword(KnownIdentityV3Hash, Password));
            verifier.VerifyDecoy(password);
            Assert.Equal(original, password);
            return hash;
        }, TestContext.Current.CancellationToken)).ToArray();

        start.SetResult();
        string[] hashes = await Task.WhenAll(tasks);
        Assert.Equal(count, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void VerificationPathsPerformComparableWork()
    {
        AccountPasswordHasher verifier = new();
        Action[] paths =
        [
            () => Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword(KnownV1Hash, "wrong")),
            () => verifier.VerifyDecoy("wrong"),
            () => Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword(KnownIdentityV3Hash, "wrong")),
            () => Assert.Equal(AccountPasswordVerificationStatus.Failed, verifier.VerifyPassword("malformed", "wrong")),
        ];

        // Warm every branch and rotate measurement order. Medians reject isolated scheduler
        // stalls; the broad two-sided bound catches a no-op or a duplicated verification path.
        foreach (Action path in paths)
        {
            path();
        }

        const int sampleCount = 7;
        double[][] samples = paths.Select(_ => new double[sampleCount]).ToArray();
        for (int sample = 0; sample < sampleCount; sample++)
        {
            for (int offset = 0; offset < paths.Length; offset++)
            {
                int index = (sample + offset) % paths.Length;
                long started = Stopwatch.GetTimestamp();
                paths[index]();
                samples[index][sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
        }

        foreach (double[] sample in samples)
        {
            Array.Sort(sample);
        }

        double real = samples[0][sampleCount / 2];
        for (int index = 1; index < paths.Length; index++)
        {
            double ratio = samples[index][sampleCount / 2] / real;
            Assert.True(ratio is > 0.5 and < 2.0,
                $"Verification path {index} median/real ratio was {ratio:F3}; expected equivalent password work.");
        }
    }

    [Fact]
    public void HashPassword_ProducesCurrentVersionedScheme()
    {
        AccountPasswordHasher verifier = new();

        string passwordHash = verifier.HashPassword(Password.AsSpan());

        Assert.StartsWith(CurrentSchemePrefix, passwordHash);

        string[] components = passwordHash.Split('$', StringSplitOptions.None);

        Assert.Equal(6, components.Length);

        Assert.Equal(string.Empty, components[0]);

        Assert.Equal("openconquer", components[1]);

        Assert.Equal("pbkdf2-sha256", components[2]);

        Assert.Equal("v=1", components[3]);

        byte[] salt = Convert.FromBase64String(components[4]);

        byte[] derivedKey = Convert.FromBase64String(components[5]);

        Assert.Equal(16, salt.Length);

        Assert.Equal(32, derivedKey.Length);
    }

    [Fact]
    public void HashPassword_UsesIndependentRandomSalt()
    {
        AccountPasswordHasher verifier = new();

        string first = verifier.HashPassword(Password.AsSpan());

        string second = verifier.HashPassword(Password.AsSpan());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void VerifyPassword_KnownV1VectorReturnsSuccess()
    {
        AccountPasswordHasher verifier = new();

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            KnownV1Hash,
            Password.AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Success, result);
    }

    [Fact]
    public void VerifyPassword_WrongPasswordReturnsFailed()
    {
        AccountPasswordHasher verifier = new();

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            KnownV1Hash,
            "WrongPassword".AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Failed, result);
    }

    [Fact]
    public void VerifyPassword_UnicodePasswordRoundTrips()
    {
        const string password = "pässwörd-密碼";

        AccountPasswordHasher verifier = new();

        string passwordHash = verifier.HashPassword(password.AsSpan());

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            passwordHash,
            password.AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Success, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(
        "$openconquer$pbkdf2-sha256$v=2$"
            + "AAECAwQFBgcICQoLDA0ODw==$"
            + "HPgEfYQxRr+Kw2c55AfeLYbvAto2AZtipPkLZlszeTQ="
    )]
    [InlineData(
        "$openconquer$pbkdf2-sha256$v=1$"
            + "!!!!!!!!!!!!!!!!!!!!!!!!$"
            + "HPgEfYQxRr+Kw2c55AfeLYbvAto2AZtipPkLZlszeTQ="
    )]
    [InlineData(KnownV1Hash + "$extra")]
    public void VerifyPassword_MalformedOrUnsupportedHashReturnsFailed(string passwordHash)
    {
        AccountPasswordHasher verifier = new();

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            passwordHash,
            Password.AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Failed, result);
    }

    [Fact]
    public void VerifyDecoy_AcceptsCallerOwnedPasswordMemory()
    {
        AccountPasswordHasher verifier = new();

        verifier.VerifyDecoy(Password.AsSpan());
    }

    [Fact]
    public void HashPassword_DoesNotModifyCallerPasswordBuffer()
    {
        char[] password = Password.ToCharArray();

        char[] original = password.ToArray();

        AccountPasswordHasher verifier = new();

        _ = verifier.HashPassword(password);

        Assert.Equal(original, password);
    }
}

[CollectionDefinition("Password verification work", DisableParallelization = true)]
public sealed class PasswordVerificationWorkCollection;
