using OpenConquer.Domain.Items;

namespace OpenConquer.Domain.Tests.Items;

public sealed class ItemLifetimeTests
{
    [Fact]
    public void Default_IsInvalid()
    {
        ItemLifetime lifetime = default;

        Assert.False(lifetime.IsValid);
        Assert.Equal((ItemLifetimeState)0, lifetime.State);
        Assert.Null(lifetime.PendingActivationDurationSeconds);
        Assert.Null(lifetime.ExpiresAtUtc);
    }

    [Fact]
    public void CreatePermanent_ReturnsValidPermanentLifetime()
    {
        ItemLifetime lifetime = ItemLifetime.CreatePermanent();

        Assert.True(lifetime.IsValid);
        Assert.Equal(ItemLifetimeState.Permanent, lifetime.State);
        Assert.Null(lifetime.PendingActivationDurationSeconds);
        Assert.Null(lifetime.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(604800)]
    [InlineData(int.MaxValue)]
    public void CreatePendingActivation_PositiveDuration_ReturnsValidPendingLifetime(int durationSeconds)
    {
        ItemLifetime lifetime = ItemLifetime.CreatePendingActivation(durationSeconds);

        Assert.True(lifetime.IsValid);
        Assert.Equal(ItemLifetimeState.PendingActivation, lifetime.State);
        Assert.Equal(durationSeconds, lifetime.PendingActivationDurationSeconds);
        Assert.Null(lifetime.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CreatePendingActivation_NonPositiveDuration_ThrowsArgumentOutOfRangeException(int durationSeconds)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ItemLifetime.CreatePendingActivation(durationSeconds));

        Assert.Equal("durationSeconds", exception.ParamName);
    }

    [Fact]
    public void CreateActiveExpiry_UtcInstant_ReturnsValidActiveLifetime()
    {
        DateTimeOffset expiresAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        ItemLifetime lifetime = ItemLifetime.CreateActiveExpiry(expiresAtUtc);

        Assert.True(lifetime.IsValid);
        Assert.Equal(ItemLifetimeState.ActiveExpiry, lifetime.State);
        Assert.Null(lifetime.PendingActivationDurationSeconds);
        Assert.Equal(expiresAtUtc, lifetime.ExpiresAtUtc);
    }

    [Fact]
    public void CreateActiveExpiry_PastUtcInstant_RemainsValid()
    {
        DateTimeOffset expiresAtUtc = DateTimeOffset.UnixEpoch;

        ItemLifetime lifetime = ItemLifetime.CreateActiveExpiry(expiresAtUtc);

        Assert.True(lifetime.IsValid);
        Assert.Equal(expiresAtUtc, lifetime.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(14)]
    public void CreateActiveExpiry_NonUtcOffset_ThrowsArgumentException(int offsetHours)
    {
        DateTimeOffset expiresAt = new(2027, 1, 2, 3, 4, 5, TimeSpan.FromHours(offsetHours));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ItemLifetime.CreateActiveExpiry(expiresAt));

        Assert.Equal("expiresAtUtc", exception.ParamName);
    }

    [Fact]
    public void EqualLifetimes_HaveValueEquality()
    {
        DateTimeOffset expiresAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.Equal(ItemLifetime.CreatePermanent(), ItemLifetime.CreatePermanent());
        Assert.Equal(ItemLifetime.CreatePendingActivation(3600), ItemLifetime.CreatePendingActivation(3600));
        Assert.Equal(ItemLifetime.CreateActiveExpiry(expiresAtUtc), ItemLifetime.CreateActiveExpiry(expiresAtUtc));
    }
}
