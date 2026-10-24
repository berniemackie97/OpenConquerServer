namespace OpenConquer.Domain.Accounts;

/// <summary>
/// Name and password rules for self-service account registration.
/// </summary>
/// <remarks>
/// These are stricter than <see cref="AccountCredentialPolicy"/>, which still governs login. The 5517 client sends
/// account names as ANSI bytes in the active code page, so only printable ASCII is accepted to keep the stored
/// name identical on every client. Its password field holds at most 15 characters and its keypad transform only
/// carries letters, digits and a few symbols; the server decodes registration through the same transform, so any
/// accepted password can be typed back at login.
/// </remarks>
public static class AccountRegistrationPolicy
{
    public const int MinimumUsernameLength = 4;
    public const int MaximumUsernameLength = 16;
    public const int MinimumPasswordLength = 6;
    public const int MaximumPasswordLength = 15;

    public static bool IsValidUsername(string? username)
    {
        if (username is null || username.Length is < MinimumUsernameLength or > MaximumUsernameLength)
        {
            return false;
        }

        foreach (char character in username)
        {
            if (character is < '!' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidPassword(ReadOnlySpan<char> password)
    {
        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
        {
            return false;
        }

        foreach (char character in password)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '@' or '.' or '>' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
