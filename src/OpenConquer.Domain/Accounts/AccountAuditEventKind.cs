namespace OpenConquer.Domain.Accounts;

public enum AccountAuditEventKind : byte
{
    AccountCreated = 1,
    AccountSuspended = 2,
    AccountReactivated = 3,
    AccountBanned = 4,
    AccountUnbanned = 5,
    AccountDeleted = 6,
    AccountRestored = 7,
    PasswordChanged = 8,
    PasswordMigrated = 9,
    AuthorityRoleChanged = 10,
}
