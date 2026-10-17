using MySqlConnector;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Characters;

/// <summary>
/// One durable character-position CAS through a dedicated, column-restricted writer.
/// Not registered with the existing read-only GameServer persistence identity.
/// </summary>
public sealed class CharacterLocationWriteStore : ICharacterLocationWriteStore
{
    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    public CharacterLocationWriteStore(string connectionString)
    {
        MySqlConnectionStringBuilder builder = new(GameDbContextOptionsConfiguration.CreateConnectionString(connectionString))
        {
            UseAffectedRows = true,
        };
        _connectionString = builder.ConnectionString;
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
                `position_y` = @target_y
            WHERE `character_id` = @character_id
              AND `account_id` = @account_id
              AND `map_id` = @expected_map_id
              AND `position_x` = @expected_x
              AND `position_y` = @expected_y;
            """;

        command.Parameters.AddWithValue("@character_id", change.CharacterId);
        command.Parameters.AddWithValue("@account_id", change.AccountId);
        command.Parameters.AddWithValue("@expected_map_id", change.Expected.MapId);
        command.Parameters.AddWithValue("@expected_x", change.Expected.X);
        command.Parameters.AddWithValue("@expected_y", change.Expected.Y);
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
                "Character-location persistence failed after dispatch. The commit outcome may be uncertain; stop the transfer and reconcile using a fresh authoritative read. Do not retry blindly.", exception);
        }
    }
}
