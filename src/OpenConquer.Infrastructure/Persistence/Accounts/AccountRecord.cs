using OpenConquer.Domain.Accounts;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountRecord
{
    public uint AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public AccountAccessStatus AccessStatus { get; set; }
    public AccountAuthorityRole AuthorityRole { get; set; }
    public Guid CreationOperationId { get; set; }
    public DateTime? LastSuccessfulLoginAtUtc { get; set; }
    public ulong StateRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AccountActorKind CreatedByActorKind { get; set; }
    public uint? CreatedByAccountId { get; set; }
    public DateTime StateChangedAtUtc { get; set; }
    public AccountActorKind StateChangedByActorKind { get; set; }
    public uint? StateChangedByAccountId { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public AccountActorKind? DeletedByActorKind { get; set; }
    public uint? DeletedByAccountId { get; set; }
    public AccountPasswordCredentialRecord PasswordCredential { get; set; } = null!;
}
