using System.Text;
using OpenConquer.Assets.IO;

namespace OpenConquer.Assets.Tests.IO;

public sealed class DecodedTextDatReaderTests
{
    [Fact]
    public void DecodeInPlace_KnownRetailSeedVector_DecodesExpectedBytes()
    {
        byte[] payload = Convert.FromHexString("9C0B8F7ADEBE1E2947F942564701568D");

        DecodedTextDatReader.DecodeInPlace(payload, 0x2537);

        Assert.Equal("100000@@Test@@\r\n"u8.ToArray(), payload);
    }

    [Fact]
    public void DecodeInPlace_CrossesSeedTableBoundary_DecodesExpectedBytes()
    {
        byte[] payload = Convert.FromHexString(
            "AD6947E39D188F8A1B21A7AD83A8960F4DA2EBDD6CE08DDD98CA711D49B8B960A145B82FD7F68964D509F4E5A8838525"
            + "93050DC42C7A95118D92205C2199CA12CBC17D9F10BF214F58DF2655ED1175DA1222C62659DCFC91EE17485C7C82A76F"
            + "72872C54A703BC62FAFC99038FCA3E64F4164051BB1999F4B49C45EA132898CD2D6845E79508AFCA");
        byte[] expected = Enumerable.Range(0, 136).Select(static value => (byte)value).ToArray();

        DecodedTextDatReader.DecodeInPlace(payload, 0x2537);

        Assert.Equal(expected, payload);
    }

    [Fact]
    public void DecodeToText_KnownVector_UsesProvidedEncoding()
    {
        byte[] payload = Convert.FromHexString("EEA9D6E547BC375585FCD991");

        string decoded = DecodedTextDatReader.DecodeToText(payload, 0x2537, Encoding.UTF8);

        Assert.Equal("Café 世界", decoded);
    }

    [Fact]
    public void DecodeLines_KnownVector_ReturnsDecodedLines()
    {
        byte[] payload = Convert.FromHexString("CBB986609A198CB076F53286054808BC3464322D8D");

        IReadOnlyList<string> lines = DecodedTextDatReader.DecodeLines(payload, 0x2537, Encoding.ASCII);

        Assert.Equal(["first", "second", "third"], lines);
    }

    [Fact]
    public void DecodeLines_EmbeddedBlankLine_PreservesBlankLine()
    {
        byte[] payload = Convert.FromHexString("EC7167937DF0");

        IReadOnlyList<string> lines = DecodedTextDatReader.DecodeLines(payload, 0x2537, Encoding.ASCII);

        Assert.Equal(["A", string.Empty, "B"], lines);
    }

    [Fact]
    public void Decode_EmptyPayload_ReturnsEmptyResults()
    {
        byte[] payload = [];

        DecodedTextDatReader.DecodeInPlace(payload, 0x2537);

        Assert.Empty(payload);
        Assert.Equal(string.Empty, DecodedTextDatReader.DecodeToText(payload, 0x2537, Encoding.UTF8));
        Assert.Empty(DecodedTextDatReader.DecodeLines(payload, 0x2537, Encoding.UTF8));
    }

    [Fact]
    public void DecodeToText_NullEncoding_ThrowsArgumentNullException()
    {
        byte[] payload = [0x01];

        Assert.Throws<ArgumentNullException>(() => DecodedTextDatReader.DecodeToText(payload, 0x2537, null!));
    }
}
