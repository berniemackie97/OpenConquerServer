namespace OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

internal static class AccountSchemaContract
{
    public const string ComponentName = "accounts";
    public const uint SchemaVersion = 1;
    public const string MigrationId = "20260906043138_InitialAccountSchema";

    public const string DatabaseCharacterSet = "utf8mb4";
    public const string DatabaseCollation = "utf8mb4_0900_as_cs";
}
