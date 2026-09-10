namespace OpenConquer.Protocol.Game.Cryptography;

/// <summary>
/// Applies the fixed one-shot CAST5 CFB-64 bootstrap transform used by the
/// native 5517 GameServer Diffie-Hellman handshake.
/// </summary>
internal static class GameHandshakeBootstrapCipher
{
    internal static ReadOnlySpan<byte> Key => "BC234xs45nme7HU9"u8;

    internal static void Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext) => Transform(plaintext, ciphertext, encrypt: true);

    internal static void Decrypt(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext) => Transform(ciphertext, plaintext, encrypt: false);

    private static void Transform(ReadOnlySpan<byte> input, Span<byte> output, bool encrypt)
    {
        Span<byte> initializationVector = stackalloc byte[GameCast5Cfb64Cipher.InitializationVectorLength];
        initializationVector.Clear();

        using GameCast5Cfb64Cipher cipher = new(Key, initializationVector, encrypt);

        cipher.Transform(input, output);
    }
}
