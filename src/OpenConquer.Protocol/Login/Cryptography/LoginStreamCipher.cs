namespace OpenConquer.Protocol.Login.Cryptography;

/// <summary>
/// Implements the stateful stream transform used by the 5517 account
/// login connection.
/// </summary>
/// <remarks>
/// The native client transform applies the generated keystream before the
/// nibble swap and 0xAB mask. The server endpoint therefore applies the
/// inverse ordering for both outbound encryption and inbound decryption.
///
/// Inbound and outbound byte positions are independent streams.
/// </remarks>
public sealed class LoginStreamCipher
{
    private const byte XorMask = 0xAB;

    private static readonly byte[] s_streamA = BuildStreamA();
    private static readonly byte[] s_streamB = BuildStreamB();

    private ushort _outboundPosition;
    private ushort _inboundPosition;

    /// <summary>
    /// Encrypts server-to-client account traffic in place.
    /// </summary>
    public void EncryptOutbound(Span<byte> buffer)
    {
        Transform(buffer, ref _outboundPosition);
    }

    /// <summary>
    /// Decrypts client-to-server account traffic in place.
    /// </summary>
    public void DecryptInbound(Span<byte> buffer)
    {
        Transform(buffer, ref _inboundPosition);
    }

    private static void Transform(Span<byte> buffer, ref ushort position)
    {
        for (int index = 0; index < buffer.Length; index++)
        {
            byte value = buffer[index];

            value ^= XorMask;
            value = SwapNibbles(value);
            value ^= s_streamA[(byte)position];
            value ^= s_streamB[(byte)(position >> 8)];

            buffer[index] = value;

            position = unchecked((ushort)(position + 1));
        }
    }

    private static byte[] BuildStreamA()
    {
        byte[] stream = new byte[byte.MaxValue + 1];

        byte key = 0x9D;

        for (int index = 0; index < stream.Length; index++)
        {
            stream[index] = key;

            byte multiplied = unchecked((byte)(key * 0xFA));

            byte added = unchecked((byte)(multiplied + 0x0F));

            key = unchecked((byte)((added * key) + 0x13));
        }

        return stream;
    }

    private static byte[] BuildStreamB()
    {
        byte[] stream = new byte[byte.MaxValue + 1];

        byte key = 0x62;

        for (int index = 0; index < stream.Length; index++)
        {
            stream[index] = key;

            byte multiplied = unchecked((byte)(key * 0x5C));

            byte subtracted = unchecked((byte)(0x79 - multiplied));

            key = unchecked((byte)((subtracted * key) + 0x6D));
        }

        return stream;
    }

    private static byte SwapNibbles(byte value)
    {
        return (byte)((value >> 4) | (value << 4));
    }
}
