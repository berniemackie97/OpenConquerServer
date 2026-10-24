using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// The decoded fields of one account-registration request.
/// </summary>
/// <remarks>
/// The password stays in a mutable buffer instead of a string so it can be wiped.
/// </remarks>
public sealed class LoginAccountRegistrationRequest : IDisposable
{
    private char[]? _password;

    internal LoginAccountRegistrationRequest(string accountName, char[] password, int passwordLength)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegative(passwordLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(passwordLength, password.Length);

        AccountName = accountName;
        PasswordLength = passwordLength;
        _password = password;
    }

    public string AccountName { get; }
    public int PasswordLength { get; }

    /// <summary>
    /// Copies the decoded password into <paramref name="destination"/>.
    /// </summary>
    public void CopyPasswordTo(Span<char> destination)
    {
        char[] password = _password ?? throw new ObjectDisposedException(nameof(LoginAccountRegistrationRequest));

        if (destination.Length < PasswordLength)
        {
            throw new ArgumentException($"Destination must contain at least {PasswordLength} characters.", nameof(destination));
        }

        password.AsSpan(0, PasswordLength).CopyTo(destination);
    }

    public void Dispose()
    {
        char[]? password = Interlocked.Exchange(ref _password, null);

        if (password is not null)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }
}
