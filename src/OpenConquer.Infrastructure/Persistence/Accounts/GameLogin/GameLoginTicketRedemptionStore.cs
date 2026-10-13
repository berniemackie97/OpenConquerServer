using System.Data;
using System.Security.Cryptography;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin.Redemption;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketRedemptionStore(MySqlDataSource dataSource, GameLoginTicketAuthenticationKeyRing authenticationKeyRing, RealmId realmId)
    : IGameLoginTicketRedemptionStore
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing = authenticationKeyRing ?? throw new ArgumentNullException(nameof(authenticationKeyRing));
    private readonly RealmId _realmId = realmId.Value != 0 ? realmId : throw new ArgumentOutOfRangeException(nameof(realmId));

    public async ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
    {
        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A game-login ticket redemption requires a nonzero session UID.");
        }

        if (authenticationKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationKey), "A game-login ticket redemption requires a nonzero authentication key.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedTicket? ticket = await LockTicketAsync(connection, transaction, sessionUid, cancellationToken).ConfigureAwait(false);

        if (ticket is null)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return null;
        }

        using (ticket)
        {
            if (ticket.RealmId != _realmId.Value)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            DateTime databaseUtcNow = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            if (databaseUtcNow >= ticket.ExpiresAtUtc)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            bool authenticationKeyMatches = _authenticationKeyRing.Verify(ticket.AuthenticationKeyVerifierKeyId, ticket.AuthenticationKeyVerifier, sessionUid, authenticationKey);

            if (!authenticationKeyMatches)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool deleted = await TryDeleteUnexpiredTicketAsync(connection, transaction, sessionUid, _realmId, cancellationToken).ConfigureAwait(false);

            if (!deleted)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            return ticket.Identity;
        }
    }

    private static async ValueTask<PersistedTicket?> LockTicketAsync(MySqlConnection connection, MySqlTransaction transaction, uint sessionUid, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                `account_id`,
                `realm_id`,
                `username`,
                `expires_at_utc`,
                `authentication_key_verifier_key_id`,
                `authentication_key_verifier`
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid
            FOR UPDATE;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        uint accountId = reader.GetUInt32(0);
        uint realmId = reader.GetUInt32(1);
        string username = reader.GetString(2);
        DateTime expiresAtUtc = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
        ushort authenticationKeyVerifierKeyId = reader.GetUInt16(4);
        byte[] authenticationKeyVerifier = reader.GetFieldValue<byte[]>(5);

        return new PersistedTicket(new GameLoginTicketIdentity(accountId, username, sessionUid), realmId, expiresAtUtc, authenticationKeyVerifierKeyId, authenticationKeyVerifier);
    }

    private static async ValueTask<DateTime> ReadDatabaseUtcNowAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result is not DateTime databaseUtcNow)
        {
            throw new InvalidOperationException("MySQL did not return a valid UTC timestamp during game-login ticket redemption.");
        }

        return DateTime.SpecifyKind(databaseUtcNow, DateTimeKind.Utc);
    }

    private static async ValueTask<bool> TryDeleteUnexpiredTicketAsync(MySqlConnection connection, MySqlTransaction transaction, uint sessionUid, RealmId realmId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid
              AND `realm_id` = @realm_id
              AND `expires_at_utc` > UTC_TIMESTAMP(6);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = realmId.Value;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return affected switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException($"Game-login ticket redemption persistence affected an unexpected number of rows. Expected at most 1, but MySQL reported {affected}."),
        };
    }

    private sealed class PersistedTicket(GameLoginTicketIdentity identity, uint realmId, DateTime expiresAtUtc, ushort authenticationKeyVerifierKeyId, byte[] authenticationKeyVerifier) : IDisposable
    {
        public GameLoginTicketIdentity Identity { get; } = identity;
        public uint RealmId { get; } = realmId;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public ushort AuthenticationKeyVerifierKeyId { get; } = authenticationKeyVerifierKeyId;
        public byte[] AuthenticationKeyVerifier { get; } = authenticationKeyVerifier;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(AuthenticationKeyVerifier);
        }
    }
}
