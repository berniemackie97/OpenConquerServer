namespace OpenConquer.Application.Accounts.Authentication;

public sealed class AccountAuthenticationSnapshot
{
    public AccountAuthenticationSnapshot(uint accountId, string username, string passwordHash, AccountLoginAccess access, ulong accountStateRevision, ulong passwordCredentialRevision)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A persisted authentication snapshot must identify an account.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "The account login access value is not supported.");
        }

        if (accountStateRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountStateRevision), "An account state revision must be greater than zero.");
        }

        if (passwordCredentialRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(passwordCredentialRevision), "A password credential revision must be greater than zero.");
        }

        AccountId = accountId;
        Username = username;
        PasswordHash = passwordHash;
        Access = access;
        AccountStateRevision = accountStateRevision;
        PasswordCredentialRevision = passwordCredentialRevision;
    }

    public uint AccountId { get; }
    public string Username { get; }
    public string PasswordHash { get; }
    public AccountLoginAccess Access { get; }
    public ulong AccountStateRevision { get; }
    public ulong PasswordCredentialRevision { get; }
}
