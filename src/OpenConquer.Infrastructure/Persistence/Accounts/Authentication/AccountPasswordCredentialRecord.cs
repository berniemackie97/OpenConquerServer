namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountPasswordCredentialRecord
{
    public uint AccountId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime PasswordChangedAtUtc { get; set; }
    public ulong Revision { get; set; }
    public AccountRecord Account { get; set; } = null!;
}
