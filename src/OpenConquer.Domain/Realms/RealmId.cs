namespace OpenConquer.Domain.Realms;

public readonly record struct RealmId
{
    public RealmId(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public uint Value { get; }
}
