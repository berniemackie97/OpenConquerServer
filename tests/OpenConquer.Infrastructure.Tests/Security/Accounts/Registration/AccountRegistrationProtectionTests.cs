using System.Net;
using OpenConquer.Infrastructure.Security.Accounts.Registration;

namespace OpenConquer.Infrastructure.Tests.Security.Accounts.Registration;

public sealed class AccountRegistrationProtectionTests
{
    private static readonly TimeSpan s_window = TimeSpan.FromHours(1);

    [Fact]
    public void ExpiredInFlightSource_DoesNotPinOtherExpiredSources()
    {
        ManualTimeProvider time = new();
        AccountRegistrationProtection protection = Create(time, attemptLimit: 1, trackedSources: 2);
        IPAddress first = IPAddress.Parse("203.0.113.1");
        IDisposable pending = Admit(protection, first);
        Admit(protection, IPAddress.Parse("203.0.113.2")).Dispose();

        time.Advance(s_window);
        Assert.False(protection.TryBeginRegistration(first, out _));
        Admit(protection, IPAddress.Parse("203.0.113.3")).Dispose();
        pending.Dispose();
        Admit(protection, first).Dispose();
    }

    [Fact]
    public void MappedIpv4_SharesTheIpv4Quota()
    {
        AccountRegistrationProtection protection = Create(new ManualTimeProvider(), attemptLimit: 1);
        Admit(protection, IPAddress.Parse("203.0.113.1")).Dispose();
        Assert.False(protection.TryBeginRegistration(IPAddress.Parse("::ffff:203.0.113.1"), out _));
    }

    [Fact]
    public void ParallelAdmissionAndDisposal_EnforceOneAttemptPerSource()
    {
        AccountRegistrationProtection protection = Create(new ManualTimeProvider(), attemptLimit: 100);
        IPAddress source = IPAddress.Parse("203.0.113.1");
        IDisposable?[] leases = new IDisposable?[64];
        Parallel.For(0, leases.Length, index => protection.TryBeginRegistration(source, out leases[index]));
        IDisposable lease = Assert.Single(leases, value => value is not null)!;
        Parallel.For(0, 64, _ => lease.Dispose());
        Admit(protection, source).Dispose();
    }

    [Fact]
    public void SourceQuota_CountsEveryAdmittedAttemptUntilTheWindowExpires()
    {
        ManualTimeProvider time = new();
        AccountRegistrationProtection protection = Create(time, attemptLimit: 2);
        IPAddress source = IPAddress.Parse("203.0.113.7");

        Admit(protection, source).Dispose();
        Admit(protection, source).Dispose();
        Assert.False(protection.TryBeginRegistration(source, out _));

        time.Advance(s_window - TimeSpan.FromSeconds(1));
        Assert.False(protection.TryBeginRegistration(source, out _));

        time.Advance(TimeSpan.FromSeconds(1));
        Admit(protection, source).Dispose();
    }

    [Fact]
    public void Source_HasOneAttemptInFlight()
    {
        AccountRegistrationProtection protection = Create(new ManualTimeProvider(), attemptLimit: 5);
        IPAddress source = IPAddress.Parse("203.0.113.7");

        IDisposable first = Admit(protection, source);
        Assert.False(protection.TryBeginRegistration(source, out _));

        first.Dispose();
        first.Dispose();
        Admit(protection, source).Dispose();
    }

    [Fact]
    public void Ipv6Sources_ShareOneQuotaPerSlash64()
    {
        AccountRegistrationProtection protection = Create(new ManualTimeProvider(), attemptLimit: 1);

        Admit(protection, IPAddress.Parse("2001:db8:1:2::10")).Dispose();

        Assert.False(protection.TryBeginRegistration(IPAddress.Parse("2001:db8:1:2:ffff::1"), out _));
        Admit(protection, IPAddress.Parse("2001:db8:1:3::10")).Dispose();
    }

    [Fact]
    public void GlobalConcurrency_BoundsPasswordHashingAcrossSources()
    {
        AccountRegistrationProtection protection = Create(new ManualTimeProvider(), attemptLimit: 5, concurrency: 2);

        IDisposable first = Admit(protection, IPAddress.Parse("203.0.113.1"));
        IDisposable second = Admit(protection, IPAddress.Parse("203.0.113.2"));
        Assert.False(protection.TryBeginRegistration(IPAddress.Parse("203.0.113.3"), out _));

        first.Dispose();
        Admit(protection, IPAddress.Parse("203.0.113.3")).Dispose();
        second.Dispose();
    }

    [Fact]
    public void TrackedSources_AreBoundedUntilWindowsExpire()
    {
        ManualTimeProvider time = new();
        AccountRegistrationProtection protection = Create(time, attemptLimit: 5, trackedSources: 2);

        Admit(protection, IPAddress.Parse("203.0.113.1")).Dispose();
        Admit(protection, IPAddress.Parse("203.0.113.2")).Dispose();
        Assert.False(protection.TryBeginRegistration(IPAddress.Parse("203.0.113.3"), out _));

        time.Advance(s_window);
        Admit(protection, IPAddress.Parse("203.0.113.3")).Dispose();
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void Options_RejectNonPositiveValues(int attempts, int windowSeconds, int concurrency, int tracked)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountRegistrationProtectionOptions(
            attempts, TimeSpan.FromSeconds(windowSeconds), concurrency, tracked));
    }

    private static AccountRegistrationProtection Create(TimeProvider time, int attemptLimit, int concurrency = 4, int trackedSources = 100)
    {
        return new AccountRegistrationProtection(new AccountRegistrationProtectionOptions(attemptLimit, s_window, concurrency, trackedSources), time);
    }

    private static IDisposable Admit(AccountRegistrationProtection protection, IPAddress source)
    {
        Assert.True(protection.TryBeginRegistration(source, out IDisposable? lease));
        return lease!;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
