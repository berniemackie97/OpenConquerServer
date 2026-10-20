using System.Data;
using MySqlConnector;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Domain.Characters;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Characters;

/// <summary>
/// One durable character-position CAS through a dedicated, column-restricted writer.
/// Not registered with the existing read-only GameServer persistence identity.
/// </summary>
public sealed class CharacterLocationWriteStore : ICharacterLocationWriteStore, ICharacterLocationObservationStore, ICharacterLocationFenceStore
{
    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    public CharacterLocationWriteStore(string connectionString)
    {
        MySqlConnectionStringBuilder builder = new(GameDbContextOptionsConfiguration.CreateConnectionString(connectionString))
        {
            UseAffectedRows = true,
            GuidFormat = MySqlGuidFormat.None, // Keep byte order explicit across all supported caller connection strings.
        };
        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Verifies all required v13 column-scoped grants before the network listener is started.
    /// The probes cannot change a character row: valid player identifiers are never zero.
    /// </summary>
    public async ValueTask VerifyPermissionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `account_id`, `character_id`, `map_id`, `position_x`, `position_y`,
                   `location_revision`, `last_location_operation_id`, `location_writer_token`
            FROM `characters` WHERE `account_id` = 0 AND `character_id` = 0 LIMIT 0;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command.CommandText = """
            UPDATE `characters`
            SET `map_id` = `map_id`,
                `position_x` = `position_x`,
                `position_y` = `position_y`,
                `location_revision` = `location_revision`,
                `last_location_operation_id` = `last_location_operation_id`,
                `location_writer_token` = `location_writer_token`
            WHERE `account_id` = 0 AND `character_id` = 0;
            """;
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidOperationException("A reserved zero identity unexpectedly matched location-writer readiness.");
        }
    }

    /// <summary>
    /// Reads the location once for the account and character. Nothing is retried, and the result says nothing
    /// about an earlier compare-and-swap.
    /// </summary>
    public async ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            SELECT `map_id`, `position_x`, `position_y`, `location_revision`, `last_location_operation_id`, `location_writer_token`
            FROM `characters`
            WHERE `account_id` = @account_id
              AND `character_id` = @character_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@character_id", characterId);

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Guid? operationId = reader.IsDBNull(4) ? null : new Guid(reader.GetFieldValue<byte[]>(4), bigEndian: true);
        Guid? writerToken = reader.IsDBNull(5) ? null : new Guid(reader.GetFieldValue<byte[]>(5), bigEndian: true);
        return new PersistedCharacterLocation(
            new(reader.GetUInt32(0), reader.GetUInt16(1), reader.GetUInt16(2)), reader.GetInt64(3),
            operationId, writerToken);
    }

    /// <summary>
    /// One atomic row mutation revokes every older write generation. A later read must
    /// still find the exact token, otherwise a concurrent acquirer already replaced it.
    /// InnoDB serializes competing CAS and rotation updates on the same character row.
    /// </summary>
    public async ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        cancellationToken.ThrowIfCancellationRequested();

        Guid token = Guid.NewGuid();
        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (MySqlCommand rotation = connection.CreateCommand())
        {
            rotation.CommandTimeout = CommandTimeoutSeconds;
            rotation.CommandText = """
                UPDATE `characters`
                SET `location_writer_token` = @writer_token
                WHERE `account_id` = @account_id
                  AND `character_id` = @character_id;
                """;
            rotation.Parameters.AddWithValue("@writer_token", token.ToByteArray(bigEndian: true));
            rotation.Parameters.AddWithValue("@account_id", accountId);
            rotation.Parameters.AddWithValue("@character_id", characterId);
            // An exception after dispatch is uncertain: callers must not authorize gameplay.
            int affected = await rotation.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            if (affected == 0)
            {
                return null;
            }
            if (affected != 1)
            {
                throw new InvalidOperationException("Character writer-fence rotation touched multiple rows.");
            }
        }

        await using MySqlCommand read = connection.CreateCommand();
        read.CommandTimeout = CommandTimeoutSeconds;
        read.CommandText = """
            SELECT `map_id`, `position_x`, `position_y`, `location_revision`, `last_location_operation_id`
            FROM `characters`
            WHERE `account_id` = @account_id
              AND `character_id` = @character_id
              AND `location_writer_token` = @writer_token
            LIMIT 1;
            """;
        read.Parameters.AddWithValue("@writer_token", token.ToByteArray(bigEndian: true));
        read.Parameters.AddWithValue("@account_id", accountId);
        read.Parameters.AddWithValue("@character_id", characterId);
        await using MySqlDataReader reader = await read.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Another acquisition superseded this one before observation.
            return null;
        }
        Guid? lastOperation = reader.IsDBNull(4) ? null :
            new Guid(reader.GetFieldValue<byte[]>(4), bigEndian: true);
        PersistedCharacterLocation snapshot = new(
            new(reader.GetUInt32(0), reader.GetUInt16(1), reader.GetUInt16(2)),
            reader.GetInt64(3), lastOperation, token);
        return new CharacterLocationWriteAuthority(accountId, characterId, token, snapshot);
    }

    public async ValueTask<CharacterLocationWriteResult> TryCommitAsync(CharacterLocationChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            UPDATE `characters`
            SET `map_id` = @target_map_id,
                `position_x` = @target_x,
                `position_y` = @target_y,
                `location_revision` = `location_revision` + 1,
                `last_location_operation_id` = @operation_id
            WHERE `character_id` = @character_id
              AND `account_id` = @account_id
              AND `map_id` = @expected_map_id
              AND `position_x` = @expected_x
              AND `position_y` = @expected_y
              AND `location_revision` = @expected_revision
              AND `location_writer_token` = @writer_token
              AND `location_revision` < 9223372036854775807;
            """;

        command.Parameters.AddWithValue("@character_id", change.CharacterId);
        command.Parameters.AddWithValue("@account_id", change.AccountId);
        command.Parameters.AddWithValue("@expected_map_id", change.Expected.MapId);
        command.Parameters.AddWithValue("@expected_x", change.Expected.X);
        command.Parameters.AddWithValue("@expected_y", change.Expected.Y);
        command.Parameters.AddWithValue("@expected_revision", change.ExpectedRevision);
        command.Parameters.AddWithValue("@writer_token", change.WriterToken.ToByteArray(bigEndian: true));
        command.Parameters.AddWithValue("@operation_id", change.OperationId.ToByteArray(bigEndian: true));
        command.Parameters.AddWithValue("@target_map_id", change.Destination.MapId);
        command.Parameters.AddWithValue("@target_x", change.Destination.X);
        command.Parameters.AddWithValue("@target_y", change.Destination.Y);

        // Past the dispatch boundary, cancellation cannot prove the write was undone.
        // Let the server enforce the command timeout and report an uncertain failure.
        try
        {
            int affected = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            return affected switch
            {
                1 => CharacterLocationWriteResult.Committed,
                0 => CharacterLocationWriteResult.Conflict,
                _ => throw new InvalidOperationException("Character-location CAS affected more than one row; its result is unsafe."),
            };
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                "Character-location persistence failed after dispatch. Keep the transfer fenced: only exact operation ID, next revision and destination provide positive commit evidence. A different observation remains unresolved; do not retry.", exception);
        }
    }
}
