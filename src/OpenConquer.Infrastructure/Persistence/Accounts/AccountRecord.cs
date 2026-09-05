namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountRecord
{
    public uint Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmailVerification { get; set; } = string.Empty;
    public byte EmailStatus { get; set; }
    public string SecurityAnswer { get; set; } = string.Empty;
    public string SecurityQuestion { get; set; } = string.Empty;
    public uint Permission { get; set; }
    public uint LoginTimestamp { get; set; }
    public string? RegistrationOperationId { get; set; }
}
