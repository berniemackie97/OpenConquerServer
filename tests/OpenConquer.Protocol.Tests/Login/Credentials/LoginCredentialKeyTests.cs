using OpenConquer.Protocol.Login.Credentials;

namespace OpenConquer.Protocol.Tests.Login.Credentials;

public sealed class LoginCredentialKeyTests
{
    [Theory]
    [InlineData(0L, "2627F6859715AD1DD294DDC476193931")]
    [InlineData(0x0012_34ABL, "EE84B012303A60D0C8394C8F3DFDD1C9")]
    [InlineData(0xFFFF_FFFFL, "232B2E854CBE858D52987198FB778977")]
    public void Derive_WritesVerifiedMsvcrtSequence(long seedValue, string expectedHex)
    {
        uint seed = checked((uint)seedValue);

        Span<byte> destination = stackalloc byte[LoginCredentialKey.Length];

        LoginCredentialKey.Derive(seed, destination);

        byte[] expected = Convert.FromHexString(expectedHex);

        Assert.Equal(expected, destination.ToArray());
    }

    [Fact]
    public void Derive_RejectsDestinationSmallerThanKey()
    {
        byte[] destination = new byte[LoginCredentialKey.Length - 1];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            LoginCredentialKey.Derive(loginSeed: 0x1234_5678, destination)
        );

        Assert.Equal("destination", exception.ParamName);

        Assert.All(destination, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Derive_DoesNotModifyBytesBeyondKey()
    {
        byte[] destination = new byte[LoginCredentialKey.Length + 4];

        Array.Fill(destination, (byte)0xCC);

        LoginCredentialKey.Derive(loginSeed: 0x1234_5678, destination);

        for (int index = LoginCredentialKey.Length; index < destination.Length; index++)
        {
            Assert.Equal(0xCC, destination[index]);
        }
    }
}
