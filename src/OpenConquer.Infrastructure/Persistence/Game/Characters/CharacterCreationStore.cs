using MySqlConnector;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Domain.Characters;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Characters;

/// <summary>
/// Atomically inserts a resolved starting character using a dedicated creation database identity.
/// </summary>
public sealed class CharacterCreationStore : ICharacterCreationStore
{
    private const int CommandTimeoutSeconds = 15;

    private readonly string _connectionString;

    public CharacterCreationStore(string connectionString)
    {
        _connectionString = GameDbContextOptionsConfiguration.CreateConnectionString(connectionString);
    }

    public async ValueTask<CharacterCreationStoreResult> CreateAsync(CharacterCreationCandidate character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            INSERT INTO `characters`
                (`account_id`,
                 `name`,
                 `appearance_composite`,
                 `hair_composite`,
                 `level`,
                 `experience`,
                 `strength`,
                 `agility`,
                 `vitality`,
                 `spirit`,
                 `unspent_attribute_points`,
                 `current_life`,
                 `current_mana`,
                 `profession`,
                 `first_profession`,
                 `previous_profession`,
                 `rebirth_count`,
                 `pre_rebirth_level`,
                 `silver`,
                 `conquer_points`,
                 `bound_conquer_points`,
                 `pk_points`,
                 `title_id`,
                 `enlightenment_points`,
                 `map_id`,
                 `position_x`,
                 `position_y`)
            VALUES
                (@account_id,
                 @name,
                 @appearance_composite,
                 @hair_composite,
                 @level,
                 0,
                 @strength,
                 @agility,
                 @vitality,
                 @spirit,
                 0,
                 @current_life,
                 @current_mana,
                 @profession,
                 @profession,
                 @profession,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 @map_id,
                 @position_x,
                 @position_y);
            """;

        command.Parameters.AddWithValue("@account_id", character.AccountId);
        command.Parameters.AddWithValue("@name", character.Name);
        command.Parameters.AddWithValue("@appearance_composite", character.AppearanceComposite);
        command.Parameters.AddWithValue("@hair_composite", character.HairComposite);
        command.Parameters.AddWithValue("@level", CharacterCreationCandidate.StartingLevel);
        command.Parameters.AddWithValue("@strength", character.Strength);
        command.Parameters.AddWithValue("@agility", character.Agility);
        command.Parameters.AddWithValue("@vitality", character.Vitality);
        command.Parameters.AddWithValue("@spirit", character.Spirit);
        command.Parameters.AddWithValue("@current_life", character.CurrentLife);
        command.Parameters.AddWithValue("@current_mana", character.CurrentMana);
        command.Parameters.AddWithValue("@profession", character.Profession);
        command.Parameters.AddWithValue("@map_id", character.MapId);
        command.Parameters.AddWithValue("@position_x", character.PositionX);
        command.Parameters.AddWithValue("@position_y", character.PositionY);

        try
        {
            int affected = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

            if (affected != 1)
            {
                throw new InvalidOperationException("Character creation did not report exactly one inserted row. The write outcome must be reconciled before retrying.");
            }

            long insertedId = command.LastInsertedId;

            if (insertedId < CharacterIdentityPolicy.FirstPlayerEntityId || insertedId > uint.MaxValue)
            {
                throw new InvalidOperationException("Character creation returned an invalid generated identity. The write may already be committed.");
            }

            return CharacterCreationStoreResult.Created(checked((uint)insertedId));
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return await ResolveDuplicateAsync(connection, character, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                "Character creation failed during insertion. Its commit outcome may be uncertain; do not retry blindly.", exception);
        }
    }

    private static async ValueTask<CharacterCreationStoreResult> ResolveDuplicateAsync(
        MySqlConnection connection, CharacterCreationCandidate character, CancellationToken cancellationToken)
    {
        await using MySqlCommand accountQuery = connection.CreateCommand();

        accountQuery.CommandTimeout = CommandTimeoutSeconds;
        accountQuery.CommandText =
            """
            SELECT `character_id`
            FROM `characters`
            WHERE `account_id` = @account_id
            LIMIT 1;
            """;

        accountQuery.Parameters.AddWithValue("@account_id", character.AccountId);

        object? accountCharacter = await accountQuery.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (accountCharacter is not null)
        {
            return CharacterCreationStoreResult.AccountAlreadyHasCharacter();
        }

        await using MySqlCommand nameQuery = connection.CreateCommand();

        nameQuery.CommandTimeout = CommandTimeoutSeconds;
        nameQuery.CommandText =
            """
            SELECT `character_id`
            FROM `characters`
            WHERE `name` = @name
            LIMIT 1;
            """;

        nameQuery.Parameters.AddWithValue("@name", character.Name);

        object? nameCharacter = await nameQuery.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (nameCharacter is not null)
        {
            return CharacterCreationStoreResult.NameAlreadyInUse();
        }

        throw new InvalidOperationException("The database rejected character creation for a duplicate key, but the conflicting character could not be resolved.");
    }
}
