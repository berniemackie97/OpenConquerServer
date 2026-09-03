using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Implements the RC5-32/12/16 block cipher used by the native 5517 login
/// credential field.
/// </summary>
/// <remarks>
/// This is protocol-compatibility cryptography. The parameters, byte order,
/// and key schedule are fixed by verified native-client behavior.
/// </remarks>
public static class LoginCredentialRc5Cipher
{
    public const int KeyLength = 16;
    public const int BlockLength = 8;

    private const int Rounds = 12;
    private const int KeyWordCount = KeyLength / sizeof(uint);
    private const int SubkeyCount = 2 * (Rounds + 1);
    private const int KeyScheduleMixIterations = 3 * SubkeyCount;

    private const uint P32 = 0xB7E15163;
    private const uint Q32 = 0x9E3779B9;

    /// <summary>
    /// Decrypts one or more complete native login credential RC5 blocks in
    /// place.
    /// </summary>
    public static void Decrypt(ReadOnlySpan<byte> key, Span<byte> buffer)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"Login credential RC5 keys must contain exactly {KeyLength} bytes.", nameof(key));
        }

        if (buffer.Length % BlockLength != 0)
        {
            throw new ArgumentException($"Login credential RC5 input must be a multiple of {BlockLength} bytes.", nameof(buffer));
        }

        Span<uint> keyWords = stackalloc uint[KeyWordCount];

        Span<uint> subkeys = stackalloc uint[SubkeyCount];

        try
        {
            ExpandKey(key, keyWords, subkeys);

            for (int offset = 0; offset < buffer.Length; offset += BlockLength)
            {
                Span<byte> block = buffer.Slice(offset, BlockLength);

                uint a = BinaryPrimitives.ReadUInt32LittleEndian(block);

                uint b = BinaryPrimitives.ReadUInt32LittleEndian(block[sizeof(uint)..]);

                for (int round = Rounds; round >= 1; round--)
                {
                    b = BitOperations.RotateRight(unchecked(b - subkeys[(2 * round) + 1]), (int)(a & 31)) ^ a;

                    a = BitOperations.RotateRight(unchecked(a - subkeys[2 * round]), (int)(b & 31)) ^ b;
                }

                b = unchecked(b - subkeys[1]);

                a = unchecked(a - subkeys[0]);

                BinaryPrimitives.WriteUInt32LittleEndian(block, a);

                BinaryPrimitives.WriteUInt32LittleEndian(block[sizeof(uint)..], b);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(keyWords));

            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(subkeys));
        }
    }

    private static void ExpandKey(ReadOnlySpan<byte> key, Span<uint> keyWords, Span<uint> subkeys)
    {
        for (int index = 0; index < keyWords.Length; index++)
        {
            keyWords[index] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(index * sizeof(uint), sizeof(uint)));
        }

        subkeys[0] = P32;

        for (int index = 1; index < subkeys.Length; index++)
        {
            subkeys[index] = unchecked(subkeys[index - 1] + Q32);
        }

        uint a = 0;
        uint b = 0;

        int subkeyIndex = 0;
        int keyWordIndex = 0;

        for (int iteration = 0; iteration < KeyScheduleMixIterations; iteration++)
        {
            a = subkeys[subkeyIndex] = BitOperations.RotateLeft(unchecked(subkeys[subkeyIndex] + a + b), 3);

            uint rotation = unchecked(a + b);

            b = keyWords[keyWordIndex] = BitOperations.RotateLeft(unchecked(keyWords[keyWordIndex] + rotation), (int)(rotation & 31));

            subkeyIndex++;

            if (subkeyIndex == subkeys.Length)
            {
                subkeyIndex = 0;
            }

            keyWordIndex++;

            if (keyWordIndex == keyWords.Length)
            {
                keyWordIndex = 0;
            }
        }
    }
}
