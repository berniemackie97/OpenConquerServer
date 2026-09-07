namespace OpenConquer.Domain.Accounts;

public static class AccountCredentialPolicy
{
    public const int MaximumUsernameLength = 32;
    public const int MaximumPasswordLength = 128;

    public static bool TryNormalizeUsername(string? suppliedUsername, out string username)
    {
        username = suppliedUsername?.Trim() ?? string.Empty;

        return username.Length is > 0 and <= MaximumUsernameLength;
    }

    public static bool IsCanonicalUsername(string? username)
    {
        if (!TryNormalizeUsername(username, out string normalizedUsername))
        {
            return false;
        }

        return string.Equals(username, normalizedUsername, StringComparison.Ordinal);
    }

    public static bool IsValidPassword(ReadOnlySpan<char> password)
    {
        return password.Length is > 0 and <= MaximumPasswordLength;
    }
}
