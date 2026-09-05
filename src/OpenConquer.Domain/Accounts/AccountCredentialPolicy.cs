namespace OpenConquer.Domain.Accounts;

/// <summary>
/// Defines account credential invariantsz
/// </summary>
public static class AccountCredentialPolicy
{
    public const int MaximumUsernameLength = 32;
    public const int MaximumPasswordLength = 128;

    /// <summary>
    /// Trims surrounding whitespace without changing case or restricting the account character set.
    /// </summary>
    public static bool TryNormalizeUsername(string? suppliedUsername, out string username)
    {
        username = suppliedUsername?.Trim() ?? string.Empty;

        return username.Length is > 0 and <= MaximumUsernameLength;
    }

    /// <summary>
    /// Validates the password verbatim, whitespace is significant and is never trimmed.
    /// </summary>
    public static bool IsValidPassword(ReadOnlySpan<char> password)
    {
        return password.Length is > 0 and <= MaximumPasswordLength;
    }
}
