using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Owns the decoded fields of one native account-login request.
/// </summary>
/// <remarks>
/// The password is intentionally retained in mutable storage rather than an
/// immutable managed string so it can be cleared deterministically.
/// </remarks>
public sealed class LoginAccountRequest : IDisposable
{
    private char[]? _password;

    internal LoginAccountRequest(
        string accountName,
        string serverName,
        char[] password,
        int passwordLength
    )
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(serverName);
        ArgumentNullException.ThrowIfNull(password);

        ArgumentOutOfRangeException.ThrowIfNegative(passwordLength);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(passwordLength, password.Length);

        AccountName = accountName;
        ServerName = serverName;
        PasswordLength = passwordLength;

        _password = password;
    }

    public string AccountName { get; }

    public string ServerName { get; }

    public int PasswordLength { get; }

    /// <summary>
    /// Copies the decoded password into caller-owned storage.
    /// </summary>
    public void CopyPasswordTo(Span<char> destination)
    {
        char[] password =
            _password ?? throw new ObjectDisposedException(nameof(LoginAccountRequest));

        if (destination.Length < PasswordLength)
        {
            throw new ArgumentException(
                $"Destination must contain at least {PasswordLength} characters.",
                nameof(destination)
            );
        }

        password.AsSpan(0, PasswordLength).CopyTo(destination);
    }

    public void Dispose()
    {
        char[]? password = Interlocked.Exchange(ref _password, null);

        if (password is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
    }
}
