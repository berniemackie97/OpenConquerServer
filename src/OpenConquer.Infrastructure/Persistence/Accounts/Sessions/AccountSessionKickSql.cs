namespace OpenConquer.Infrastructure.Persistence.Accounts.Sessions;

internal static class AccountSessionKickSql
{
    /// <summary>
    /// Asks realm <c>@realm_id</c> to disconnect account <c>@account_id</c>. A repeated request refreshes the
    /// existing one, as TQ's AccountServer resent its kick on every refused retry. The statement reads no column,
    /// so the AccountServer needs only <c>INSERT</c> and <c>UPDATE</c> on the table.
    /// </summary>
    public const string Request = """
        INSERT INTO `account_session_kicks` (`realm_id`, `account_id`, `requested_at_utc`)
        VALUES (@realm_id, @account_id, UTC_TIMESTAMP(6))
        ON DUPLICATE KEY UPDATE `requested_at_utc` = UTC_TIMESTAMP(6);
        """;
}
