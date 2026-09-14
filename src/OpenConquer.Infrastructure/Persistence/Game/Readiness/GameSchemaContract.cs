namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

internal static class GameSchemaContract
{
    public const string ComponentName = "game";
    public const uint SchemaVersion = 1;
    public const string MigrationId = "20260914210346_InitialGameSchema";

    public const string DatabaseCharacterSet = "utf8mb4";
    public const string DatabaseCollation = "utf8mb4_0900_as_cs";
}
