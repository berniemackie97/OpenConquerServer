using System.Data;
using System.Security.Cryptography;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketRedemptionStore(MySqlDataSource dataSource, GameLoginTicketAuthenticationKeyRing authenticationKeyRing)
    : IGameLoginTicketRedemptionStore
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing = authenticationKeyRing ?? throw new ArgumentNullException(nameof(authenticationKeyRing));

    public async ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default)
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
            if (attemptedAtUtc.UtcDateTime >= ticket.ExpiresAtUtc)
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

            await DeleteTicketAsync(connection, transaction, sessionUid, cancellationToken).ConfigureAwait(false);

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
        string username = reader.GetString(1);
        DateTime expiresAtUtc = reader.GetDateTime(2);
        ushort authenticationKeyVerifierKeyId = reader.GetUInt16(3);

        GameLoginTicketIdentity identity = new(accountId, username, sessionUid);

        byte[] authenticationKeyVerifier = reader.GetFieldValue<byte[]>(4);

        return new PersistedTicket(identity, expiresAtUtc, authenticationKeyVerifierKeyId, authenticationKeyVerifier);
    }

    private static async ValueTask DeleteTicketAsync(MySqlConnection connection, MySqlTransaction transaction, uint sessionUid, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException($"Game-login ticket redemption persistence affected an unexpected number of rows. Expected 1, but MySQL reported {affected}.");
        }
    }

    private sealed class PersistedTicket(GameLoginTicketIdentity identity, DateTime expiresAtUtc, ushort authenticationKeyVerifierKeyId, byte[] authenticationKeyVerifier) : IDisposable
    {
        public GameLoginTicketIdentity Identity { get; } = identity;

        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;

        public ushort AuthenticationKeyVerifierKeyId { get; } = authenticationKeyVerifierKeyId;

        public byte[] AuthenticationKeyVerifier { get; } = authenticationKeyVerifier;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(AuthenticationKeyVerifier);
        }
    }
}
