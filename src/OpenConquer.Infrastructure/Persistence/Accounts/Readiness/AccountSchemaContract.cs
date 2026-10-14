namespace OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

internal static class AccountSchemaContract
{
    public const string ComponentName = "accounts";
    public const uint SchemaVersion = 5;
    public const string MigrationId = "20261013070000_AddRealmStatus";

    public const string DatabaseCharacterSet = "utf8mb4";
    public const string DatabaseCollation = "utf8mb4_0900_as_cs";
}
