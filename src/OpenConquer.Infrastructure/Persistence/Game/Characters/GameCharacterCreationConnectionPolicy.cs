using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence.Game.Characters;

/// <summary>
/// Validates the configured database identity used exclusively for character insertion.
/// Database grants remain an independently administered and verified deployment boundary.
/// </summary>
public static class GameCharacterCreationConnectionPolicy
{
    public static void Validate(string gameConnectionString, string creationConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(creationConnectionString);

        MySqlConnectionStringBuilder game;
        MySqlConnectionStringBuilder creation;

        try
        {
            game = new MySqlConnectionStringBuilder(gameConnectionString);
            creation = new MySqlConnectionStringBuilder(creationConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // A driver exception can contain connection-string material; never propagate it.
            throw new InvalidOperationException("The GameServer database connection settings are invalid.");
        }

        if (string.IsNullOrWhiteSpace(game.Database) || string.IsNullOrWhiteSpace(creation.Database)
            || !string.Equals(game.Database, creation.Database, StringComparison.Ordinal)
            || !string.Equals(game.Server, creation.Server, StringComparison.OrdinalIgnoreCase)
            || game.Port != creation.Port
            || game.ConnectionProtocol != creation.ConnectionProtocol
            || (game.ConnectionProtocol == MySqlConnectionProtocol.NamedPipe
                && !string.Equals(game.PipeName, creation.PipeName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Character creation must target the same Game database endpoint as the read-only GameServer connection.");
        }

        if (string.IsNullOrWhiteSpace(creation.UserID)
            || string.Equals(creation.UserID, "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(creation.UserID, game.UserID, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Character creation requires a separate, non-root database identity.");
        }
    }
}
