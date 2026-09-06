namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class SchemaCompatibilityRecord
{
    public string ComponentName { get; set; } = string.Empty;
    public uint SchemaVersion { get; set; }
    public string MigrationId { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }
}
