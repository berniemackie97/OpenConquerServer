using System.Data;
using System.Security.Cryptography;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketGrantStore(MySqlDataSource dataSource, GameLoginTicketAuthenticationKeyRing authenticationKeyRing)
    : IGameLoginTicketGrantStore
{
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing = authenticationKeyRing ?? throw new ArgumentNullException(nameof(authenticationKeyRing));

    public async ValueTask<GameLoginTicketGrantStatus> TryGrantAsync(GameLoginTicket ticket, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (expectedAccountStateRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedAccountStateRevision), "The expected account state revision must be greater than zero.");
        }

        if (expectedPasswordCredentialRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPasswordCredentialRevision), "The expected password credential revision must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        bool accountMatches = await LockAndValidateAccountAsync(connection, transaction, ticket, expectedAccountStateRevision, cancellationToken).ConfigureAwait(false);

        if (!accountMatches)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            return GameLoginTicketGrantStatus.AuthenticationStateChanged;
        }

        bool credentialMatches = await LockAndValidatePasswordCredentialAsync(connection, transaction, ticket.AccountId, expectedPasswordCredentialRevision, cancellationToken).ConfigureAwait(false);

        if (!credentialMatches)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            return GameLoginTicketGrantStatus.AuthenticationStateChanged;
        }

        byte[] authenticationKeyVerifier = _authenticationKeyRing.CreateVerifier(ticket.SessionUid, ticket.AuthenticationKey);

        try
        {
            try
            {
                await InsertTicketAsync(connection, transaction, ticket, authenticationKeyVerifier, _authenticationKeyRing.ActiveKeyId, cancellationToken).ConfigureAwait(false);
            }
            catch (MySqlException exception) when (exception.Number == DuplicateKeyErrorNumber)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                return GameLoginTicketGrantStatus.SessionUidCollision;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            return GameLoginTicketGrantStatus.Granted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticationKeyVerifier);
        }
    }

    private static async ValueTask<bool> LockAndValidateAccountAsync(MySqlConnection connection, MySqlTransaction transaction, GameLoginTicket ticket, ulong expectedAccountStateRevision, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                `username`,
                `access_status`,
                `deleted_at_utc`,
                `state_revision`
            FROM `accounts`
            WHERE `account_id` = @account_id
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = ticket.AccountId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        string username = reader.GetString(0);
        byte accessStatus = reader.GetByte(1);
        bool deleted = !reader.IsDBNull(2);
        ulong stateRevision = reader.GetUInt64(3);

        return string.Equals(username, ticket.Username, StringComparison.Ordinal) && accessStatus == (byte)AccountAccessStatus.Active
            && !deleted && stateRevision == expectedAccountStateRevision;
    }

    private static async ValueTask<bool> LockAndValidatePasswordCredentialAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT `revision`
            FROM `account_password_credentials`
            WHERE `account_id` = @account_id
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        ulong revision = reader.GetUInt64(0);

        return revision == expectedPasswordCredentialRevision;
    }

    private static async ValueTask InsertTicketAsync(MySqlConnection connection, MySqlTransaction transaction, GameLoginTicket ticket, byte[] authenticationKeyVerifier, ushort authenticationKeyVerifierKeyId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `game_login_tickets`
                (`session_uid`,
                 `account_id`,
                 `username`,
                 `issued_at_utc`,
                 `expires_at_utc`,
                 `authentication_key_verifier`,
                 `authentication_key_verifier_key_id`)
            VALUES
                (@session_uid,
                 @account_id,
                 @username,
                 @issued_at_utc,
                 @expires_at_utc,
                 @authentication_key_verifier,
                 @authentication_key_verifier_key_id);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = ticket.SessionUid;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = ticket.AccountId;
        command.Parameters.Add("@username", MySqlDbType.VarChar, AccountCredentialPolicy.MaximumUsernameLength).Value = ticket.Username;
        command.Parameters.Add("@issued_at_utc", MySqlDbType.DateTime).Value = ticket.IssuedAtUtc.UtcDateTime;
        command.Parameters.Add("@expires_at_utc", MySqlDbType.DateTime).Value = ticket.ExpiresAtUtc.UtcDateTime;
        command.Parameters.Add("@authentication_key_verifier", MySqlDbType.Binary, GameLoginTicketAuthenticationKeyVerifier.VerifierSize).Value = authenticationKeyVerifier;
        command.Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16).Value = authenticationKeyVerifierKeyId;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException($"Game-login ticket grant persistence affected an unexpected number of rows. Expected 1, but MySQL reported {affected}.");
        }
    }
}
