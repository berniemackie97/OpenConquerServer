using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Mutations;

public sealed class AccountMutationContext
{
    private AccountMutationContext(AccountActorKind actorKind, Guid correlationId, string? reasonCode)
    {
        if (actorKind is not AccountActorKind.System and not AccountActorKind.Migration)
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind), "The account mutation actor kind is unsupported by this boundary.");
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An account mutation requires a nonempty correlation ID.", nameof(correlationId));
        }

        if (!AccountAuditPolicy.IsValidReasonCode(reasonCode))
        {
            throw new ArgumentException($"An account mutation reason code must contain 1-{AccountAuditPolicy.MaximumReasonCodeLength} printable ASCII characters.", nameof(reasonCode));
        }

        ActorKind = actorKind;
        CorrelationId = correlationId;
        ReasonCode = reasonCode;
    }

    public AccountActorKind ActorKind { get; }
    public Guid CorrelationId { get; }
    public string? ReasonCode { get; }

    public static AccountMutationContext ForSystem(Guid correlationId, string? reasonCode = null)
    {
        return new AccountMutationContext(AccountActorKind.System, correlationId, reasonCode);
    }

    public static AccountMutationContext ForMigration(Guid correlationId, string? reasonCode = null)
    {
        return new AccountMutationContext(AccountActorKind.Migration, correlationId, reasonCode);
    }
}
