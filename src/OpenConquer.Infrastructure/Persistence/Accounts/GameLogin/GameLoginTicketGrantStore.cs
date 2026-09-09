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

    public async ValueTask<GameLoginTicketGrantResult> TryGrantAsync(GameLoginTicketGrantRequest request, TimeSpan ticketLifetime, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long ticketLifetimeMicroseconds = ValidateAndGetTicketLifetimeMicroseconds(ticketLifetime);

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

        bool accountMatches = await LockAndValidateAccountAsync(connection, transaction, request, expectedAccountStateRevision, cancellationToken).ConfigureAwait(false);

        if (!accountMatches)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return GameLoginTicketGrantResult.AuthenticationStateChanged();
        }

        bool credentialMatches = await LockAndValidatePasswordCredentialAsync(connection, transaction, request.AccountId, expectedPasswordCredentialRevision, cancellationToken).ConfigureAwait(false);

        if (!credentialMatches)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return GameLoginTicketGrantResult.AuthenticationStateChanged();
        }

        byte[] authenticationKeyVerifier = _authenticationKeyRing.CreateVerifier(request.SessionUid, request.AuthenticationKey);

        try
        {
            try
            {
                await InsertTicketAsync(connection, transaction, request, ticketLifetimeMicroseconds, authenticationKeyVerifier, _authenticationKeyRing.ActiveKeyId, cancellationToken).ConfigureAwait(false);
            }
            catch (MySqlException exception) when (exception.Number == DuplicateKeyErrorNumber)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return GameLoginTicketGrantResult.SessionUidCollision();
            }

            GameLoginTicket ticket = await ReadInsertedTicketAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);

            if (ticket.ExpiresAtUtc - ticket.IssuedAtUtc != ticketLifetime)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException("MySQL persisted a game-login ticket with an unexpected lifetime.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            return GameLoginTicketGrantResult.Granted(ticket);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticationKeyVerifier);
        }
    }

    private static long ValidateAndGetTicketLifetimeMicroseconds(TimeSpan ticketLifetime)
    {
        if (ticketLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ticketLifetime), "The game-login ticket lifetime must be greater than zero.");
        }

        if (ticketLifetime.Ticks % TimeSpan.TicksPerMicrosecond != 0)
        {
            throw new ArgumentException("The game-login ticket lifetime must be representable at MySQL microsecond precision.", nameof(ticketLifetime));
        }

        return ticketLifetime.Ticks / TimeSpan.TicksPerMicrosecond;
    }

    private static async ValueTask<bool> LockAndValidateAccountAsync(MySqlConnection connection, MySqlTransaction transaction, GameLoginTicketGrantRequest request, ulong expectedAccountStateRevision, CancellationToken cancellationToken)
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

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = request.AccountId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        string username = reader.GetString(0);
        byte accessStatus = reader.GetByte(1);
        bool deleted = !reader.IsDBNull(2);
        ulong stateRevision = reader.GetUInt64(3);

        return string.Equals(username, request.Username, StringComparison.Ordinal) && accessStatus == (byte)AccountAccessStatus.Active
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

        return reader.GetUInt64(0) == expectedPasswordCredentialRevision;
    }

    private static async ValueTask InsertTicketAsync(MySqlConnection connection, MySqlTransaction transaction, GameLoginTicketGrantRequest request, long ticketLifetimeMicroseconds, byte[] authenticationKeyVerifier, ushort authenticationKeyVerifierKeyId, CancellationToken cancellationToken)
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
                 UTC_TIMESTAMP(6),
                 TIMESTAMPADD(MICROSECOND, @ticket_lifetime_microseconds, UTC_TIMESTAMP(6)),
                 @authentication_key_verifier,
                 @authentication_key_verifier_key_id);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = request.SessionUid;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = request.AccountId;
        command.Parameters.Add("@username", MySqlDbType.VarChar, AccountCredentialPolicy.MaximumUsernameLength).Value = request.Username;
        command.Parameters.Add("@ticket_lifetime_microseconds", MySqlDbType.Int64).Value = ticketLifetimeMicroseconds;
        command.Parameters.Add("@authentication_key_verifier", MySqlDbType.Binary, GameLoginTicketAuthenticationKeyVerifier.VerifierSize).Value = authenticationKeyVerifier;
        command.Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16).Value = authenticationKeyVerifierKeyId;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException($"Game-login ticket grant persistence affected an unexpected number of rows. Expected 1, but MySQL reported {affected}.");
        }
    }

    private static async ValueTask<GameLoginTicket> ReadInsertedTicketAsync(MySqlConnection connection, MySqlTransaction transaction, GameLoginTicketGrantRequest request, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                `issued_at_utc`,
                `expires_at_utc`
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = request.SessionUid;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("MySQL did not return the game-login ticket immediately after inserting it.");
        }

        DateTime issuedAtUtcValue = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
        DateTime expiresAtUtcValue = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);

        DateTimeOffset issuedAtUtc = new(issuedAtUtcValue);
        DateTimeOffset expiresAtUtc = new(expiresAtUtcValue);

        return new GameLoginTicket(request.AccountId, request.Username, request.SessionUid, request.AuthenticationKey, issuedAtUtc, expiresAtUtc);
    }
}
