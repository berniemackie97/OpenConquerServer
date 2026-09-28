namespace OpenConquer.Domain.Items;

public enum ItemLifetimeState : byte
{
    Permanent = 1,
    PendingActivation = 2,
    ActiveExpiry = 3,
}
