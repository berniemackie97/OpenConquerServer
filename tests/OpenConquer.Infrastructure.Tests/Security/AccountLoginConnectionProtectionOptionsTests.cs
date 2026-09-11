using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class AccountLoginConnectionProtectionOptionsTests
{
    [Fact]
    public void Constructor_DefaultMatchesProductionPolicy()
    {
        AccountLoginConnectionProtectionOptions options = new();

        Assert.Equal(4, options.MaximumConcurrentConnectionsPerSource);
    }

    [Fact]
    public void Constructor_CustomValueIsPreserved()
    {
        AccountLoginConnectionProtectionOptions options = new(maximumConcurrentConnectionsPerSource: 8);

        Assert.Equal(8, options.MaximumConcurrentConnectionsPerSource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_025)]
    public void Constructor_InvalidPerSourceConcurrencyThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountLoginConnectionProtectionOptions(maximumConcurrentConnectionsPerSource: value)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_024)]
    public void Constructor_SupportedBoundaryValuesAreAccepted(int value)
    {
        AccountLoginConnectionProtectionOptions options = new(maximumConcurrentConnectionsPerSource: value);

        Assert.Equal(value, options.MaximumConcurrentConnectionsPerSource);
    }
}
