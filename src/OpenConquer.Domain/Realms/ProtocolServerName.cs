namespace OpenConquer.Domain.Realms;

public readonly record struct ProtocolServerName
{
    public const int MaximumLength = 15;

    public ProtocolServerName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length is < 1 or > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A realm protocol name must contain 1–15 ASCII bytes.");
        }

        foreach (char character in value)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '(' or ')' or '-')
            {
                continue;
            }

            throw new ArgumentException("A realm protocol name may contain only ASCII letters, digits, underscore, parentheses and hyphen.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}
