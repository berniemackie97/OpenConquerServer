using System.Text;

namespace OpenConquer.GameData.Tool.Formats.Text;

public static class DecodedTextDatReader
{
    private const int SeedTableLength = 128;

    public static string OpenAndDecodeToText(string path, int seed, Encoding encoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(encoding);

        return DecodeToText(File.ReadAllBytes(path), seed, encoding);
    }

    public static IReadOnlyList<string> OpenAndDecodeLines(string path, int seed, Encoding encoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(encoding);

        return DecodeLines(File.ReadAllBytes(path), seed, encoding);
    }

    public static string DecodeToText(ReadOnlySpan<byte> encodedPayload, int seed, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        if (encodedPayload.IsEmpty)
        {
            return string.Empty;
        }

        byte[] decodedPayload = encodedPayload.ToArray();
        DecodeInPlace(decodedPayload, seed);
        return encoding.GetString(decodedPayload);
    }

    public static IReadOnlyList<string> DecodeLines(ReadOnlySpan<byte> encodedPayload, int seed, Encoding encoding)
    {
        string decodedText = DecodeToText(encodedPayload, seed, encoding);
        List<string> lines = [];

        using StringReader reader = new(decodedText);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    public static void DecodeInPlace(Span<byte> payload, int seed)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        Span<byte> seedTable = stackalloc byte[SeedTableLength];
        BuildSeedTable(seedTable, seed);

        for (int index = 0; index < payload.Length; index++)
        {
            byte transformed = (byte)(payload[index] ^ seedTable[index % SeedTableLength]);
            int rotation = index & 7;
            payload[index] = rotation == 0 ? transformed : RotateRight(transformed, rotation);
        }
    }

    private static void BuildSeedTable(Span<byte> seedTable, int seed)
    {
        uint state = unchecked((uint)seed);

        for (int index = 0; index < seedTable.Length; index++)
        {
            state = unchecked(state * 214013u + 2531011u);
            seedTable[index] = (byte)(((state >> 16) & 0x7FFFu) % 256u);
        }
    }

    private static byte RotateRight(byte value, int bitCount)
    {
        return (byte)((value >> bitCount) | (value << (8 - bitCount)));
    }
}
