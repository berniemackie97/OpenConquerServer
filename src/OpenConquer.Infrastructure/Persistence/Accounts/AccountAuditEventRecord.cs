using OpenConquer.Domain.Accounts;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountAuditEventRecord
{
    public ulong AccountAuditEventId { get; set; }
    public uint AccountId { get; set; }
    public AccountAuditEventKind EventKind { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public AccountActorKind ActorKind { get; set; }
    public uint? ActorAccountId { get; set; }
    public Guid CorrelationId { get; set; }
    public string? ReasonCode { get; set; }
    public AccountAccessStatus? PreviousAccessStatus { get; set; }
    public AccountAccessStatus? NewAccessStatus { get; set; }
    public AccountAuthorityRole? PreviousAuthorityRole { get; set; }
    public AccountAuthorityRole? NewAuthorityRole { get; set; }
    public AccountRecord Account { get; set; } = null!;
}
