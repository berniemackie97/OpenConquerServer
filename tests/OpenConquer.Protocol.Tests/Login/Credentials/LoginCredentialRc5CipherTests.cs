using OpenConquer.Protocol.Login.Credentials;

namespace OpenConquer.Protocol.Tests.Login.Credentials;

public sealed class LoginCredentialRc5CipherTests
{
    [Fact]
    public void Decrypt_MatchesPublishedRc5_32_12_16Vector()
    {
        byte[] key = new byte[LoginCredentialRc5Cipher.KeyLength];

        byte[] ciphertext = Convert.FromHexString("21A5DBEE154B8F6D");

        LoginCredentialRc5Cipher.Decrypt(key, ciphertext);

        Assert.Equal(new byte[LoginCredentialRc5Cipher.BlockLength], ciphertext);
    }

    [Fact]
    public void Decrypt_MatchesSeedDerivedProfileAcrossMultipleBlocks()
    {
        Span<byte> key = stackalloc byte[LoginCredentialKey.Length];

        LoginCredentialKey.Derive(loginSeed: 0x0012_34AB, key);

        byte[] ciphertext = Convert.FromHexString(
            "5414DBD63EDA5EB5" + "D89C58F3471BCA90" + "A496EB0AF967B93B" + "CB491E4BC62527B2"
        );

        LoginCredentialRc5Cipher.Decrypt(key, ciphertext);

        Assert.Equal(
            Convert.FromHexString(
                "0102030405060708" + "090A0B0C0D0E0F10" + "1112131415161718" + "191A1B1C1D1E1F20"
            ),
            ciphertext
        );
    }

    [Fact]
    public void Decrypt_RejectsInvalidKeyLengthWithoutMutatingInput()
    {
        byte[] key = new byte[LoginCredentialRc5Cipher.KeyLength - 1];

        byte[] ciphertext = Convert.FromHexString("21A5DBEE154B8F6D");

        byte[] original = ciphertext.ToArray();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            LoginCredentialRc5Cipher.Decrypt(key, ciphertext)
        );

        Assert.Equal("key", exception.ParamName);

        Assert.Equal(original, ciphertext);
    }

    [Fact]
    public void Decrypt_RejectsPartialBlockWithoutMutatingInput()
    {
        byte[] key = new byte[LoginCredentialRc5Cipher.KeyLength];

        byte[] ciphertext = new byte[LoginCredentialRc5Cipher.BlockLength + 1];

        Array.Fill(ciphertext, (byte)0xCC);

        byte[] original = ciphertext.ToArray();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            LoginCredentialRc5Cipher.Decrypt(key, ciphertext)
        );

        Assert.Equal("buffer", exception.ParamName);

        Assert.Equal(original, ciphertext);
    }
}
