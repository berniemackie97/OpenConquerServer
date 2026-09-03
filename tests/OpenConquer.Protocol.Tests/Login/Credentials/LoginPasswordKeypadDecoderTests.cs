using System.Text;
using OpenConquer.Protocol.Login.Credentials;

namespace OpenConquer.Protocol.Tests.Login.Credentials;

public sealed class LoginPasswordKeypadDecoderTests
{
    [Theory]
    [InlineData("testacc", "2165AAAA5AC76B7E92", "password1")]
    [InlineData("bernie1", "75F9ECF5694F64B3", "Test1234")]
    [InlineData("abc", "686346F547", "a-2.z")]
    [InlineData("Alice", "C6F1F5", "abc")]
    [InlineData("Alice", "C4", "1")]
    [InlineData("Alice", "17", "-")]
    [InlineData("Alice", "97", "_")]
    public void Decode_MatchesVerifiedNativeVectors(
        string accountName,
        string encodedHex,
        string expectedPassword
    )
    {
        byte[] accountNameBytes = Encoding.ASCII.GetBytes(accountName);

        byte[] encoded = Convert.FromHexString(encodedHex);

        Span<char> destination = stackalloc char[encoded.Length];

        int written = LoginPasswordKeypadDecoder.Decode(accountNameBytes, encoded, destination);

        Assert.Equal(expectedPassword, new string(destination[..written]));
    }

    [Fact]
    public void Decode_UsesSignedAccountBytesForSeed()
    {
        byte[] accountNameBytes = [0xE9];

        byte[] encoded = Convert.FromHexString("8515B489");

        Span<char> destination = stackalloc char[encoded.Length];

        int written = LoginPasswordKeypadDecoder.Decode(accountNameBytes, encoded, destination);

        Assert.Equal("abc1", new string(destination[..written]));
    }

    [Fact]
    public void Decode_StopsAtZeroTerminator()
    {
        byte[] accountNameBytes = Encoding.ASCII.GetBytes("Alice");

        byte[] encoded = Convert.FromHexString("C6F1F500C4");

        Span<char> destination = stackalloc char[encoded.Length];

        int written = LoginPasswordKeypadDecoder.Decode(accountNameBytes, encoded, destination);

        Assert.Equal("abc", new string(destination[..written]));
    }

    [Fact]
    public void Decode_StopsAtUnmappedNativeValue()
    {
        byte[] accountNameBytes = Encoding.ASCII.GetBytes("testacc");

        byte[] encoded = [0x02];

        Span<char> destination = stackalloc char[encoded.Length];

        destination.Fill('?');

        int written = LoginPasswordKeypadDecoder.Decode(accountNameBytes, encoded, destination);

        Assert.Equal(0, written);

        Assert.Equal('?', destination[0]);
    }

    [Fact]
    public void Decode_RejectsSmallDestinationWithoutMutation()
    {
        byte[] accountNameBytes = Encoding.ASCII.GetBytes("Alice");

        byte[] encoded = Convert.FromHexString("C6F1F5");

        char[] destination = ['X', 'X'];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            LoginPasswordKeypadDecoder.Decode(accountNameBytes, encoded, destination)
        );

        Assert.Equal("destination", exception.ParamName);

        Assert.Equal(new[] { 'X', 'X' }, destination);
    }
}
