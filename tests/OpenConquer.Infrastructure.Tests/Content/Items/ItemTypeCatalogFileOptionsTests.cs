using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.Tests.Content.Items;

public sealed class ItemTypeCatalogFileOptionsTests
{
    [Fact]
    public void Constructor_ValidOptions_PreservesConfiguration()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "item-types.json");

        ItemTypeCatalogFileOptions options = new(filePath, maximumFileLengthBytes: 1024, maximumDefinitions: 100);

        Assert.Equal(Path.GetFullPath(filePath), options.FilePath);
        Assert.Equal(1024, options.MaximumFileLengthBytes);
        Assert.Equal(100, options.MaximumDefinitions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidFilePath_ThrowsArgumentException(string? filePath)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ItemTypeCatalogFileOptions(filePath!, maximumFileLengthBytes: 1024, maximumDefinitions: 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidMaximumFileLength_ThrowsArgumentOutOfRangeException(int maximumFileLengthBytes)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ItemTypeCatalogFileOptions("item-types.json", maximumFileLengthBytes, maximumDefinitions: 100));

        Assert.Equal("maximumFileLengthBytes", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidMaximumDefinitions_ThrowsArgumentOutOfRangeException(int maximumDefinitions)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ItemTypeCatalogFileOptions("item-types.json", maximumFileLengthBytes: 1024, maximumDefinitions));

        Assert.Equal("maximumDefinitions", exception.ParamName);
    }
}
