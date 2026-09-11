using System.Net;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class AccountLoginConnectionProtectionTests
{
    [Fact]
    public void TryBeginConnection_NullRemoteAddressThrows()
    {
        AccountLoginConnectionProtection protection = CreateProtection();

        Assert.Throws<ArgumentNullException>(() => protection.TryBeginConnection(null!, out _));
    }

    [Fact]
    public void TryBeginConnection_RejectsSourceAtConcurrencyLimit()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 2);
        IPAddress address = IPAddress.Parse("192.0.2.10");

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? first));
        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? second));
        Assert.False(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? rejected));
        Assert.Null(rejected);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void TryBeginConnection_ReleasedLeaseRestoresSourceCapacity()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);
        IPAddress address = IPAddress.Parse("192.0.2.10");

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? first));
        Assert.False(protection.TryBeginConnection(address, out _));

        first.Dispose();

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? second));

        second.Dispose();
    }

    [Fact]
    public void TryBeginConnection_DifferentSourcesHaveIndependentCapacity()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);

        Assert.True(protection.TryBeginConnection(IPAddress.Parse("192.0.2.10"), out IAccountLoginConnectionLease? first));
        Assert.True(protection.TryBeginConnection(IPAddress.Parse("192.0.2.11"), out IAccountLoginConnectionLease? second));

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void TryBeginConnection_Ipv4MappedIpv6SharesIpv4Capacity()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);
        IPAddress ipv4 = IPAddress.Parse("192.0.2.10");
        IPAddress mappedIpv6 = IPAddress.Parse("::ffff:192.0.2.10");

        Assert.True(protection.TryBeginConnection(ipv4, out IAccountLoginConnectionLease? connection));
        Assert.False(protection.TryBeginConnection(mappedIpv6, out _));

        connection.Dispose();
    }

    [Fact]
    public void TryBeginConnection_Ipv6AddressesWithinSamePrefixShareCapacity()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);
        IPAddress firstAddress = IPAddress.Parse("2001:db8:1234:5678::1");
        IPAddress secondAddress = IPAddress.Parse("2001:db8:1234:5678::ffff");

        Assert.True(protection.TryBeginConnection(firstAddress, out IAccountLoginConnectionLease? connection));
        Assert.False(protection.TryBeginConnection(secondAddress, out _));

        connection.Dispose();
    }

    [Fact]
    public void TryBeginConnection_DifferentIpv6PrefixesHaveIndependentCapacity()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);

        Assert.True(protection.TryBeginConnection(IPAddress.Parse("2001:db8:1234:5678::1"), out IAccountLoginConnectionLease? first));
        Assert.True(protection.TryBeginConnection(IPAddress.Parse("2001:db8:1234:5679::1"), out IAccountLoginConnectionLease? second));

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Lease_DisposeIsIdempotent()
    {
        AccountLoginConnectionProtection protection = CreateProtection(maximumConcurrentConnectionsPerSource: 1);
        IPAddress address = IPAddress.Parse("192.0.2.10");

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? first));

        first.Dispose();
        first.Dispose();

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? second));
        Assert.False(protection.TryBeginConnection(address, out _));

        second.Dispose();
    }

    [Fact]
    public async Task TryBeginConnection_ConcurrentCallsCannotExceedSourceLimit()
    {
        const int concurrencyLimit = 4;
        const int contenderCount = 32;

        AccountLoginConnectionProtection protection = CreateProtection(concurrencyLimit);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ManualResetEventSlim start = new(false);

        Task<IAccountLoginConnectionLease?>[] contenders = Enumerable.Range(0, contenderCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();

                return protection.TryBeginConnection(address, out IAccountLoginConnectionLease? connection)
                    ? connection
                    : null;
            }))
            .ToArray();

        start.Set();

        IAccountLoginConnectionLease?[] results = await Task.WhenAll(contenders);
        IAccountLoginConnectionLease[] admitted = results.OfType<IAccountLoginConnectionLease>().ToArray();

        Assert.Equal(concurrencyLimit, admitted.Length);

        foreach (IAccountLoginConnectionLease connection in admitted)
        {
            connection.Dispose();
        }

        Assert.True(protection.TryBeginConnection(address, out IAccountLoginConnectionLease? finalConnection));

        finalConnection.Dispose();
    }

    private static AccountLoginConnectionProtection CreateProtection(int maximumConcurrentConnectionsPerSource = 4)
    {
        return new AccountLoginConnectionProtection(new AccountLoginConnectionProtectionOptions(maximumConcurrentConnectionsPerSource));
    }
}
