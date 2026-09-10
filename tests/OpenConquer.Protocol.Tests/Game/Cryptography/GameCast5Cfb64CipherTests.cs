using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Tests.Game.Cryptography;

public sealed class GameCast5Cfb64CipherTests
{
    private static ReadOnlySpan<byte> BootstrapKey => "BC234xs45nme7HU9"u8;

    [Fact]
    public void Transform_MatchesVerifiedOpenConquerPublicCast5Cfb64Vector()
    {
        byte[] plaintext = new byte[8];
        byte[] actual = new byte[plaintext.Length];

        using GameCast5Cfb64Cipher cipher = new(BootstrapKey, Convert.FromHexString("0123456789ABCDEF"), encrypt: true);

        cipher.Transform(plaintext, actual);

        Assert.Equal(Convert.FromHexString("9264FD04E1E8882C"), actual);
    }

    [Fact]
    public void Transform_MatchesIndependentZeroIvCast5Cfb64Vector()
    {
        byte[] plaintext = "OpenConquer-5517-CFB64"u8.ToArray();
        byte[] actual = new byte[plaintext.Length];

        using GameCast5Cfb64Cipher cipher = new(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: true);

        cipher.Transform(plaintext, actual);

        Assert.Equal(Convert.FromHexString("6A13741CE64645FCEC5A9D7A9630A3D18A9F3539C28C"), actual);
    }

    [Fact]
    public void Transform_DecryptsVerifiedCast5Cfb64Vector()
    {
        byte[] ciphertext = Convert.FromHexString("6A13741CE64645FCEC5A9D7A9630A3D18A9F3539C28C");
        byte[] actual = new byte[ciphertext.Length];

        using GameCast5Cfb64Cipher cipher = new(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: false);

        cipher.Transform(ciphertext, actual);

        Assert.Equal("OpenConquer-5517-CFB64"u8.ToArray(), actual);
    }

    [Fact]
    public void Transform_PreservesStateAcrossChunkedCalls()
    {
        byte[] plaintext = Enumerable.Range(0, 97).Select(static value => unchecked((byte)((value * 37) + 11))).ToArray();
        byte[] initializationVector = Convert.FromHexString("123456789ABCDEF0");
        byte[] oneShot = new byte[plaintext.Length];
        byte[] chunked = new byte[plaintext.Length];

        using (GameCast5Cfb64Cipher cipher = new(BootstrapKey, initializationVector, encrypt: true))
        {
            cipher.Transform(plaintext, oneShot);
        }

        using (GameCast5Cfb64Cipher cipher = new(BootstrapKey, initializationVector, encrypt: true))
        {
            cipher.Transform(plaintext.AsSpan(0, 3), chunked.AsSpan(0, 3));
            cipher.Transform(plaintext.AsSpan(3, 17), chunked.AsSpan(3, 17));
            cipher.Transform(plaintext.AsSpan(20, 1), chunked.AsSpan(20, 1));
            cipher.Transform(plaintext.AsSpan(21, 51), chunked.AsSpan(21, 51));
            cipher.Transform(plaintext.AsSpan(72), chunked.AsSpan(72));
        }

        Assert.Equal(oneShot, chunked);
    }

    [Fact]
    public void Transform_SupportsInPlaceOperation()
    {
        byte[] buffer = "in-place-game-cipher"u8.ToArray();
        byte[] original = buffer.ToArray();
        byte[] initializationVector = [1, 2, 3, 4, 5, 6, 7, 8];

        using (GameCast5Cfb64Cipher cipher = new(BootstrapKey, initializationVector, encrypt: true))
        {
            cipher.Transform(buffer, buffer);
        }

        using (GameCast5Cfb64Cipher cipher = new(BootstrapKey, initializationVector, encrypt: false))
        {
            cipher.Transform(buffer, buffer);
        }

        Assert.Equal(original, buffer);
    }

    [Fact]
    public void Transform_TruncatesSessionKeyMaterialToNative128BitCastKey()
    {
        byte[] sessionKeyMaterial = Enumerable.Range(1, 64).Select(static value => (byte)value).ToArray();
        byte[] truncatedKey = sessionKeyMaterial[..16];
        byte[] plaintext = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        byte[] fullResult = new byte[plaintext.Length];
        byte[] truncatedResult = new byte[plaintext.Length];
        byte[] initializationVector = new byte[GameCast5Cfb64Cipher.InitializationVectorLength];

        using (GameCast5Cfb64Cipher cipher = new(sessionKeyMaterial, initializationVector, encrypt: true))
        {
            cipher.Transform(plaintext, fullResult);
        }

        using (GameCast5Cfb64Cipher cipher = new(truncatedKey, initializationVector, encrypt: true))
        {
            cipher.Transform(plaintext, truncatedResult);
        }

        Assert.Equal(truncatedResult, fullResult);
    }

    [Fact]
    public void Transform_RejectsPartiallyOverlappingBuffers()
    {
        byte[] buffer = new byte[16];

        using GameCast5Cfb64Cipher cipher = new(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: true);

        Assert.Throws<ArgumentException>(() => cipher.Transform(buffer.AsSpan(0, 8), buffer.AsSpan(1, 8)));
    }

    [Fact]
    public void Transform_RejectsOutputSmallerThanInput()
    {
        using GameCast5Cfb64Cipher cipher = new(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: true);

        Assert.Throws<ArgumentException>(() => cipher.Transform(new byte[8], new byte[7]));
    }

    [Fact]
    public void Constructor_RejectsEmptyKeyOrInvalidInitializationVector()
    {
        Assert.Throws<ArgumentException>(() => new GameCast5Cfb64Cipher([], new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: true));
        Assert.Throws<ArgumentException>(() => new GameCast5Cfb64Cipher(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength - 1], encrypt: true));
    }

    [Fact]
    public void Transform_RejectsUseAfterDispose()
    {
        GameCast5Cfb64Cipher cipher = new(BootstrapKey, new byte[GameCast5Cfb64Cipher.InitializationVectorLength], encrypt: true);

        cipher.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cipher.Transform(new byte[1], new byte[1]));
    }
}
