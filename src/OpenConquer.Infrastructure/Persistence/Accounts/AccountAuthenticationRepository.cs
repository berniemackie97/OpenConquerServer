using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

public sealed class AccountAuthenticationRepository(IDbContextFactory<AccountDbContext> contextFactory) : IAccountAuthenticationRepository
{
    private readonly IDbContextFactory<AccountDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        cancellationToken.ThrowIfCancellationRequested();

        await using AccountDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        AccountAuthenticationSnapshot? account = await db.Accounts.AsNoTracking().Where(candidate => candidate.Username == accountName)
            .Select(candidate => new AccountAuthenticationSnapshot(candidate.Id, candidate.Username, candidate.PasswordHash, MapAccess(candidate.Permission)))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return account;
    }

    public async ValueTask<bool> TryRecordLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash, uint loginTimestamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ValidatePasswordHash(account.PasswordHash, nameof(account));
        if (replacementPasswordHash is not null)
        {
            ValidatePasswordHash(replacementPasswordHash, nameof(replacementPasswordHash));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (account.Access != AccountLoginAccess.Allowed)
        {
            return false;
        }

        await using AccountDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string passwordHash = replacementPasswordHash ?? account.PasswordHash;

        int affected = await db.Accounts.Where(candidate => candidate.Id == account.AccountId && EF.Functions.Hex(candidate.Username) == EF.Functions.Hex(account.Username)
                && EF.Functions.Hex(candidate.PasswordHash) == EF.Functions.Hex(account.PasswordHash) && candidate.Permission >= 1 && candidate.Permission <= 5)
            .ExecuteUpdateAsync(update => update
                .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                .SetProperty(candidate => candidate.LoginTimestamp, loginTimestamp), cancellationToken).ConfigureAwait(false);

        return affected switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException("Authentication updated more than one account."),
        };
    }

    private static AccountLoginAccess MapAccess(uint permission)
    {
        return permission switch
        {
            1 or 2 or 3 or 4 or 5 => AccountLoginAccess.Allowed,
            255 => AccountLoginAccess.Banned,
            _ => AccountLoginAccess.Denied,
        };
    }

    private static void ValidatePasswordHash(string hash, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash, parameterName);
        if (hash.Length > AccountConfiguration.MaximumPasswordHashLength)
        {
            throw new ArgumentException("A password hash cannot exceed 255 characters.", parameterName);
        }
    }
}
