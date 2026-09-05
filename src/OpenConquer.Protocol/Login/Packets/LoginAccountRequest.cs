using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Owns the decoded fields of one account login request.
/// </summary>
public sealed class LoginAccountRequest : IDisposable
{
    private char[]? _password;

    internal LoginAccountRequest(string accountName, string serverName, char[] password, int passwordLength)
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

    public void CopyPasswordTo(Span<char> destination)
    {
        char[] password = _password ?? throw new ObjectDisposedException(nameof(LoginAccountRequest));

        if (destination.Length < PasswordLength)
        {
            throw new ArgumentException($"Destination must contain at least {PasswordLength} characters.", nameof(destination));
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
