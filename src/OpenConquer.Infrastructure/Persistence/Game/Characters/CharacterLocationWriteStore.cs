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
public sealed class CharacterLocationWriteStore : ICharacterLocationWriteStore, ICharacterLocationObservationStore
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
            SELECT `map_id`, `position_x`, `position_y`, `location_revision`, `last_location_operation_id`
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
        return new PersistedCharacterLocation(
            new(reader.GetUInt32(0), reader.GetUInt16(1), reader.GetUInt16(2)), reader.GetInt64(3), operationId);
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
              AND `location_revision` < 9223372036854775807;
            """;

        command.Parameters.AddWithValue("@character_id", change.CharacterId);
        command.Parameters.AddWithValue("@account_id", change.AccountId);
        command.Parameters.AddWithValue("@expected_map_id", change.Expected.MapId);
        command.Parameters.AddWithValue("@expected_x", change.Expected.X);
        command.Parameters.AddWithValue("@expected_y", change.Expected.Y);
        command.Parameters.AddWithValue("@expected_revision", change.ExpectedRevision);
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
