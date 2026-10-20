using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence.Game.Characters;

/// <summary>
/// Ensures the location writer is a distinct, non-administrative identity targeting precisely
/// the same realm database as the read-only profile connection. MySQL grants remain authoritative.
/// </summary>
public static class GameCharacterLocationWriteConnectionPolicy
{
    public static void Validate(string gameConnectionString, string writerConnectionString,
        string? creationConnectionString = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(writerConnectionString);

        MySqlConnectionStringBuilder game;
        MySqlConnectionStringBuilder writer;
        MySqlConnectionStringBuilder? creation = null;

        try
        {
            game = new(gameConnectionString);
            writer = new(writerConnectionString);
            if (creationConnectionString is not null)
            {
                creation = new(creationConnectionString);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // Driver messages may contain connection-string material; never propagate them.
            throw new InvalidOperationException("Invalid GameServer persistence connection configuration.");
        }

        if (string.IsNullOrWhiteSpace(game.Database)
            || !string.Equals(game.Database, writer.Database, StringComparison.Ordinal)
            || !string.Equals(game.Server, writer.Server, StringComparison.OrdinalIgnoreCase)
            || game.Port != writer.Port
            || game.ConnectionProtocol != writer.ConnectionProtocol
            || (game.ConnectionProtocol == MySqlConnectionProtocol.NamedPipe
                && !string.Equals(game.PipeName, writer.PipeName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The location writer must use the configured Game database endpoint.");
        }

        if (string.IsNullOrWhiteSpace(writer.UserID)
            || string.IsNullOrWhiteSpace(writer.Password)
            || string.Equals(writer.UserID, "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(writer.UserID, game.UserID, StringComparison.OrdinalIgnoreCase)
            || (creation is not null && string.Equals(writer.UserID, creation.UserID, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The location writer requires a separate, non-root, password-protected database identity.");
        }
    }
}
