namespace OpenConquer.Domain.Accounts;

public enum AccountActorKind : byte
{
    /// <summary>
    /// The action originated before an account identity existed.
    /// </summary>
    SelfService = 1,

    /// <summary>
    /// The action was performed by an existing authenticated account.
    /// </summary>
    Account = 2,

    /// <summary>
    /// The action was performed automatically by the system.
    /// </summary>
    System = 3,

    /// <summary>
    /// The action was performed by data migration tooling.
    /// </summary>
    Migration = 4,
}
