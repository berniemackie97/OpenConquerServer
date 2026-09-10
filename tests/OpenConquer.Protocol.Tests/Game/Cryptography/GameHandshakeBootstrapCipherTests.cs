using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Tests.Game.Cryptography;

public sealed class GameHandshakeBootstrapCipherTests
{
    [Fact]
    public void Encrypt_UsesNative5517BootstrapKeyAndZeroInitializationVector()
    {
        byte[] plaintext = "OpenConquer-5517-CFB64"u8.ToArray();
        byte[] ciphertext = new byte[plaintext.Length];

        GameHandshakeBootstrapCipher.Encrypt(plaintext, ciphertext);

        Assert.Equal(Convert.FromHexString("6A13741CE64645FCEC5A9D7A9630A3D18A9F3539C28C"), ciphertext);
    }

    [Fact]
    public void Decrypt_ReversesNativeBootstrapTransform()
    {
        byte[] ciphertext = Convert.FromHexString("6A13741CE64645FCEC5A9D7A9630A3D18A9F3539C28C");
        byte[] plaintext = new byte[ciphertext.Length];

        GameHandshakeBootstrapCipher.Decrypt(ciphertext, plaintext);

        Assert.Equal("OpenConquer-5517-CFB64"u8.ToArray(), plaintext);
    }

    [Fact]
    public void Encrypt_ResetsBootstrapStateForEveryEnvelope()
    {
        byte[] plaintext = Enumerable.Range(0, 64).Select(static value => unchecked((byte)((value * 29) + 7))).ToArray();
        byte[] first = new byte[plaintext.Length];
        byte[] second = new byte[plaintext.Length];

        GameHandshakeBootstrapCipher.Encrypt(plaintext, first);
        GameHandshakeBootstrapCipher.Encrypt(plaintext, second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Decrypt_ResetsBootstrapStateForEveryEnvelope()
    {
        byte[] plaintext = Enumerable.Range(0, 64).Select(static value => unchecked((byte)((value * 31) + 11))).ToArray();
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] first = new byte[plaintext.Length];
        byte[] second = new byte[plaintext.Length];

        GameHandshakeBootstrapCipher.Encrypt(plaintext, ciphertext);
        GameHandshakeBootstrapCipher.Decrypt(ciphertext, first);
        GameHandshakeBootstrapCipher.Decrypt(ciphertext, second);

        Assert.Equal(plaintext, first);
        Assert.Equal(first, second);
    }
}
