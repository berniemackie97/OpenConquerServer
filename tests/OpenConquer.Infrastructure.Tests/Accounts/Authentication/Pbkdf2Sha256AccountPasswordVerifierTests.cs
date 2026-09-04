using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Accounts.Authentication;

public sealed class Pbkdf2Sha256AccountPasswordVerifierTests
{
    private const string Password = "Test1234";

    private const string CurrentSchemePrefix = "$openconquer$pbkdf2-sha256$v=1$";

    // PBKDF2-HMAC-SHA256:
    // password = "Test1234"
    // salt     = 000102030405060708090A0B0C0D0E0F
    // rounds   = 600000
    // dkLen    = 32
    private const string KnownV1Hash = "$openconquer$pbkdf2-sha256$v=1$AAECAwQFBgcICQoLDA0ODw==$HPgEfYQxRr+Kw2c55AfeLYbvAto2AZtipPkLZlszeTQ=";

    [Fact]
    public void HashPassword_ProducesCurrentVersionedScheme()
    {
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

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
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

        string first = verifier.HashPassword(Password.AsSpan());

        string second = verifier.HashPassword(Password.AsSpan());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void VerifyPassword_KnownV1VectorReturnsSuccess()
    {
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            KnownV1Hash,
            Password.AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Success, result);
    }

    [Fact]
    public void VerifyPassword_WrongPasswordReturnsFailed()
    {
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

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

        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

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
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

        AccountPasswordVerificationStatus result = verifier.VerifyPassword(
            passwordHash,
            Password.AsSpan()
        );

        Assert.Equal(AccountPasswordVerificationStatus.Failed, result);
    }

    [Fact]
    public void VerifyDecoy_AcceptsCallerOwnedPasswordMemory()
    {
        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

        verifier.VerifyDecoy(Password.AsSpan());
    }

    [Fact]
    public void HashPassword_DoesNotModifyCallerPasswordBuffer()
    {
        char[] password = Password.ToCharArray();

        char[] original = password.ToArray();

        Pbkdf2Sha256AccountPasswordVerifier verifier = new();

        _ = verifier.HashPassword(password);

        Assert.Equal(original, password);
    }
}
