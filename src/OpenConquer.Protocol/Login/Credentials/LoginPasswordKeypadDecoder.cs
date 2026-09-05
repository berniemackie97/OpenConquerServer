using OpenConquer.Protocol.Compatibility;

namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Reverses the per account password keypad permutation.
/// </summary>
public static class LoginPasswordKeypadDecoder
{
    private const int KeyEntryCount = 0x100;
    private const int SaltLength = 16;
    private const int ShiftedScanCodeBias = 0x80;

    private readonly record struct KeypadRow(byte ScanCode, char Shifted, char Plain);

    private static readonly KeypadRow[] s_keypadRows =
    [
        new(ScanCode: 0x02, Shifted: '1', Plain: '1'),
        new(ScanCode: 0x03, Shifted: '@', Plain: '2'),
        new(ScanCode: 0x04, Shifted: '3', Plain: '3'),
        new(ScanCode: 0x05, Shifted: '4', Plain: '4'),
        new(ScanCode: 0x06, Shifted: '5', Plain: '5'),
        new(ScanCode: 0x07, Shifted: '6', Plain: '6'),
        new(ScanCode: 0x08, Shifted: '7', Plain: '7'),
        new(ScanCode: 0x09, Shifted: '8', Plain: '8'),
        new(ScanCode: 0x0A, Shifted: '9', Plain: '9'),
        new(ScanCode: 0x0B, Shifted: '0', Plain: '0'),
        new(ScanCode: 0x4F, Shifted: '1', Plain: '1'),
        new(ScanCode: 0x50, Shifted: '2', Plain: '2'),
        new(ScanCode: 0x51, Shifted: '3', Plain: '3'),
        new(ScanCode: 0x4B, Shifted: '4', Plain: '4'),
        new(ScanCode: 0x4C, Shifted: '5', Plain: '5'),
        new(ScanCode: 0x4D, Shifted: '6', Plain: '6'),
        new(ScanCode: 0x47, Shifted: '7', Plain: '7'),
        new(ScanCode: 0x48, Shifted: '8', Plain: '8'),
        new(ScanCode: 0x49, Shifted: '9', Plain: '9'),
        new(ScanCode: 0x52, Shifted: '0', Plain: '0'),
        new(ScanCode: 0x1E, Shifted: 'A', Plain: 'a'),
        new(ScanCode: 0x30, Shifted: 'B', Plain: 'b'),
        new(ScanCode: 0x2E, Shifted: 'C', Plain: 'c'),
        new(ScanCode: 0x20, Shifted: 'D', Plain: 'd'),
        new(ScanCode: 0x12, Shifted: 'E', Plain: 'e'),
        new(ScanCode: 0x21, Shifted: 'F', Plain: 'f'),
        new(ScanCode: 0x22, Shifted: 'G', Plain: 'g'),
        new(ScanCode: 0x23, Shifted: 'H', Plain: 'h'),
        new(ScanCode: 0x17, Shifted: 'I', Plain: 'i'),
        new(ScanCode: 0x24, Shifted: 'J', Plain: 'j'),
        new(ScanCode: 0x25, Shifted: 'K', Plain: 'k'),
        new(ScanCode: 0x26, Shifted: 'L', Plain: 'l'),
        new(ScanCode: 0x32, Shifted: 'M', Plain: 'm'),
        new(ScanCode: 0x31, Shifted: 'N', Plain: 'n'),
        new(ScanCode: 0x18, Shifted: 'O', Plain: 'o'),
        new(ScanCode: 0x19, Shifted: 'P', Plain: 'p'),
        new(ScanCode: 0x10, Shifted: 'Q', Plain: 'q'),
        new(ScanCode: 0x13, Shifted: 'R', Plain: 'r'),
        new(ScanCode: 0x1F, Shifted: 'S', Plain: 's'),
        new(ScanCode: 0x14, Shifted: 'T', Plain: 't'),
        new(ScanCode: 0x16, Shifted: 'U', Plain: 'u'),
        new(ScanCode: 0x2F, Shifted: 'V', Plain: 'v'),
        new(ScanCode: 0x11, Shifted: 'W', Plain: 'w'),
        new(ScanCode: 0x2D, Shifted: 'X', Plain: 'x'),
        new(ScanCode: 0x15, Shifted: 'Y', Plain: 'y'),
        new(ScanCode: 0x2C, Shifted: 'Z', Plain: 'z'),
        new(ScanCode: 0x34, Shifted: '>', Plain: '.'),
        new(ScanCode: 0x0C, Shifted: '_', Plain: '-'),
    ];

    /// <summary>
    /// Decodes the keypad bytes into password characters.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> accountNameBytes, ReadOnlySpan<byte> source, Span<char> destination)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException($"Destination must contain at least {source.Length} characters.", nameof(destination));
        }

        Span<byte> valueByIndex = stackalloc byte[KeyEntryCount];

        BuildPermutation(accountNameBytes, valueByIndex);

        int written = 0;

        foreach (byte cipherByte in source)
        {
            if (cipherByte == 0)
            {
                break;
            }

            byte value = valueByIndex[cipherByte];

            if (!TryMapValue(value, out char character))
            {
                break;
            }

            destination[written++] = character;
        }

        return written;
    }

    private static void BuildPermutation(ReadOnlySpan<byte> accountNameBytes, Span<byte> valueByIndex)
    {
        int seed = 0;

        foreach (byte value in accountNameBytes)
        {
            seed += unchecked((sbyte)value);
        }

        MsvcCrtRandom random = new(unchecked((uint)seed));

        Span<byte> salt = stackalloc byte[SaltLength];

        random.FillBytes(salt);

        Span<byte> weights = stackalloc byte[KeyEntryCount];

        valueByIndex[0] = 0;
        weights[0] = 0;

        for (int index = 1; index < KeyEntryCount; index++)
        {
            valueByIndex[index] = (byte)index;

            weights[index] = (byte)(index ^ salt[index & (SaltLength - 1)]);
        }

        for (int index = 1; index < KeyEntryCount; index++)
        {
            for (int candidate = index + 1; candidate < KeyEntryCount; candidate++)
            {
                if (weights[index] >= weights[candidate])
                {
                    continue;
                }

                (valueByIndex[index], valueByIndex[candidate]) = (valueByIndex[candidate], valueByIndex[index]);

                (weights[index], weights[candidate]) = (weights[candidate], weights[index]);
            }
        }
    }

    private static bool TryMapValue(byte value, out char character)
    {
        bool shifted = value >= ShiftedScanCodeBias;

        byte scanCode = shifted ? (byte)(value - ShiftedScanCodeBias) : value;

        foreach (KeypadRow row in s_keypadRows)
        {
            if (row.ScanCode != scanCode)
            {
                continue;
            }

            character = shifted ? row.Shifted : row.Plain;

            return true;
        }

        character = '\0';

        return false;
    }
}
