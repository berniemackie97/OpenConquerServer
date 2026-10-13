using System.Net;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Domain.Tests.Realms;

public sealed class ProtocolServerNameTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("Server_001")]
    [InlineData("Fire(World)-01")]
    [InlineData("ABCDEFGHIJKLMNO")]
    public void Constructor_ValidNativeProtocolName_PreservesExactIdentity(string value)
    {
        ProtocolServerName name = new(value);
        Assert.Equal(value, name.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    [InlineData("Main World")]
    [InlineData("naïve")]
    [InlineData("../server")]
    [InlineData("Name\\Path")]
    [InlineData("Name.With.Dot")]
    [InlineData("a\n")]
    public void Constructor_InvalidNativeProtocolName_IsRejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProtocolServerName(value));
    }

    [Fact]
    public void Constructor_IsCaseSensitiveAndNeverNormalizes()
    {
        Assert.NotEqual(new ProtocolServerName("Fire"), new ProtocolServerName("fire"));
    }

    [Fact]
    public void RealmRegistration_InvalidAndDefaultValues_FailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealmId(0));
        Assert.Throws<ArgumentException>(() => new RealmRegistration(default, new ProtocolServerName("Fire"), IPAddress.Loopback, 5816, RealmAvailability.Enabled));
        Assert.Throws<ArgumentException>(() => new RealmRegistration(new RealmId(1), default, IPAddress.Loopback, 5816, RealmAvailability.Enabled));
        Assert.Throws<ArgumentException>(() => new RealmRegistration(new RealmId(1), new ProtocolServerName("Fire"), IPAddress.IPv6Loopback, 5816, RealmAvailability.Enabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealmRegistration(new RealmId(1), new ProtocolServerName("Fire"), IPAddress.Loopback, 0, RealmAvailability.Enabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealmRegistration(new RealmId(1), new ProtocolServerName("Fire"), IPAddress.Loopback, 5816, (RealmAvailability)9));
    }
}
