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

        if (!ContainsOnlyProtocolCharacters(value))
        {
            throw new ArgumentException("A realm protocol name may contain only ASCII letters, digits, underscore, parentheses and hyphen.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Validates an untrusted name, such as one read from a login packet, without throwing.</summary>
    public static bool TryCreate(string? value, out ProtocolServerName name)
    {
        if (value is null || value.Length is < 1 or > MaximumLength || !ContainsOnlyProtocolCharacters(value))
        {
            name = default;
            return false;
        }

        name = new ProtocolServerName(value);
        return true;
    }

    private static bool ContainsOnlyProtocolCharacters(string value)
    {
        foreach (char character in value)
        {
            if (character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '(' or ')' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public string Value
    {
        get;
    }
    public override string ToString() => Value ?? string.Empty;
}
