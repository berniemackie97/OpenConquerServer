namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

internal static class GameSchemaContract
{
    public const string ComponentName = "game";
    public const uint SchemaVersion = 3;
    public const string MigrationId = "20260923223920_RetainPreRebirthLevel";

    public const string DatabaseCharacterSet = "utf8mb4";
    public const string DatabaseCollation = "utf8mb4_0900_as_cs";
}
