namespace OpenConquer.Domain.Items;

public readonly record struct ItemLifetime
{
    private ItemLifetime(ItemLifetimeState state, int? pendingActivationDurationSeconds, DateTimeOffset? expiresAtUtc)
    {
        State = state;
        PendingActivationDurationSeconds = pendingActivationDurationSeconds;
        ExpiresAtUtc = expiresAtUtc;
    }

    public ItemLifetimeState State { get; }
    public int? PendingActivationDurationSeconds { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }

    public bool IsValid => State switch
    {
        ItemLifetimeState.Permanent => PendingActivationDurationSeconds is null && ExpiresAtUtc is null,
        ItemLifetimeState.PendingActivation => PendingActivationDurationSeconds is > 0 && ExpiresAtUtc is null,
        ItemLifetimeState.ActiveExpiry => PendingActivationDurationSeconds is null
                                          && ExpiresAtUtc is { } expiresAtUtc
                                          && expiresAtUtc.Offset == TimeSpan.Zero,
        _ => false,
    };

    public static ItemLifetime CreatePermanent()
    {
        return new ItemLifetime(ItemLifetimeState.Permanent, pendingActivationDurationSeconds: null, expiresAtUtc: null);
    }

    public static ItemLifetime CreatePendingActivation(int durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "A pending item lifetime requires a positive duration.");
        }

        return new ItemLifetime(ItemLifetimeState.PendingActivation, durationSeconds, expiresAtUtc: null);
    }

    public static ItemLifetime CreateActiveExpiry(DateTimeOffset expiresAtUtc)
    {
        if (expiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("An active item lifetime expiration must use the UTC offset.", nameof(expiresAtUtc));
        }

        return new ItemLifetime(ItemLifetimeState.ActiveExpiry, pendingActivationDurationSeconds: null, expiresAtUtc);
    }
}
