using OpenConquer.GameData.Tool.IO;

namespace OpenConquer.GameData.Tool.Tests.IO;

public sealed class ToolSourceFileReaderTests
{
    [Fact]
    public void Read_ValidSource_ReturnsExactContent()
    {
        byte[] expected = [0, 1, 127, 128, 255];
        using TemporaryFile file = new(expected);

        byte[] result = ToolSourceFileReader.Read(file.Path, expected.Length);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_SourceAtMaximumLength_IsAccepted()
    {
        byte[] expected = new byte[4096];
        Array.Fill(expected, (byte)0xA5);
        using TemporaryFile file = new(expected);

        byte[] result = ToolSourceFileReader.Read(file.Path, 4096);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_SourceExceedsMaximumLength_IsRejected()
    {
        using TemporaryFile file = new(new byte[4097]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ToolSourceFileReader.Read(file.Path, 4096));

        Assert.Contains("exceeds the maximum", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_EmptySource_IsRejected()
    {
        using TemporaryFile file = new([]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ToolSourceFileReader.Read(file.Path, 4096));

        Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MissingSource_IsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");

        Assert.Throws<FileNotFoundException>(() => ToolSourceFileReader.Read(path, 4096));
    }

    [Fact]
    public void Read_InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => ToolSourceFileReader.Read(" ", 4096));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolSourceFileReader.Read("source.dat", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolSourceFileReader.Read("source.dat", -1));
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(byte[] payload)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-source-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, payload);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
