namespace OpenConquer.Application.Accounts.Authentication;

public interface IAccountAuthenticationRepository
{
    ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default);
    ValueTask<bool> TryRecordSuccessfulLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash, DateTimeOffset successfulLoginAt, CancellationToken cancellationToken = default);
}
