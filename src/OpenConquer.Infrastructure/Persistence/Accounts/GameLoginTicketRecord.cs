namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class GameLoginTicketRecord
{
    public uint SessionUid { get; set; }
    public uint AuthenticationKey { get; set; }
    public uint AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public AccountRecord Account { get; set; } = null!;
}
