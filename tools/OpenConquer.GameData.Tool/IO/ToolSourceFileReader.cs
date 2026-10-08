namespace OpenConquer.GameData.Tool.IO;

internal static class ToolSourceFileReader
{
    public static byte[] Read(string path, int maximumLengthBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (maximumLengthBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLengthBytes), maximumLengthBytes, "Maximum source length must be positive.");
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
        long length = stream.Length;

        if (length == 0)
        {
            throw new InvalidDataException("Source file is empty.");
        }

        if (length > maximumLengthBytes)
        {
            throw new InvalidDataException($"Source file length {length} exceeds the maximum of {maximumLengthBytes} bytes.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)length));

        try
        {
            stream.ReadExactly(payload);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Source file changed while it was being read.", exception);
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Source file changed while it was being read.");
        }

        return payload;
    }
}
