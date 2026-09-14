namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

internal sealed class GameDatabaseReadinessException : InvalidOperationException
{
    public GameDatabaseReadinessException(string message) : base(message) { }
    public GameDatabaseReadinessException(string message, Exception innerException) : base(message, innerException) { }
}
