using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

public sealed class AccountAuthenticationRepository(IDbContextFactory<AccountDbContext> contextFactory) : IAccountAuthenticationRepository
{
    private readonly IDbContextFactory<AccountDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        cancellationToken.ThrowIfCancellationRequested();

        await using AccountDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        AccountAuthenticationSnapshot? snapshot = await db.Accounts.AsNoTracking().Where(candidate => candidate.Username == accountName)
            .Select(candidate => new AccountAuthenticationSnapshot(
                candidate.AccountId,
                candidate.Username,
                candidate.PasswordCredential.PasswordHash,
                candidate.DeletedAtUtc != null ? AccountLoginAccess.Denied
                    : candidate.AccessStatus == AccountAccessStatus.Active
                        ? AccountLoginAccess.Allowed
                    : candidate.AccessStatus == AccountAccessStatus.Banned
                        ? AccountLoginAccess.Banned
                    : AccountLoginAccess.Denied,
                candidate.StateRevision,
                candidate.PasswordCredential.Revision
            )).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return snapshot;
    }

    public async ValueTask<bool> TryRecordSuccessfulLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash, DateTimeOffset successfulLoginAt, CancellationToken cancellationToken = default)
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

        DateTime successfulLoginAtUtc = successfulLoginAt.UtcDateTime;

        int affected;
        int expectedAffected;

        if (replacementPasswordHash is null)
        {
            affected = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `accounts` AS `a`
                    INNER JOIN `account_password_credentials` AS `c`
                        ON `c`.`account_id` = `a`.`account_id`
                    SET `a`.`last_successful_login_at_utc` =
                        CASE
                            WHEN `a`.`last_successful_login_at_utc` IS NULL
                                OR `a`.`last_successful_login_at_utc` < {successfulLoginAtUtc}
                            THEN {successfulLoginAtUtc}
                            ELSE `a`.`last_successful_login_at_utc`
                        END
                    WHERE `a`.`account_id` = {account.AccountId}
                      AND `a`.`username` COLLATE utf8mb4_0900_bin =
                          {account.Username} COLLATE utf8mb4_0900_bin
                      AND `a`.`access_status` = {(byte)AccountAccessStatus.Active}
                      AND `a`.`deleted_at_utc` IS NULL
                      AND `a`.`state_revision` = {account.AccountStateRevision}
                      AND `c`.`password_hash` = {account.PasswordHash}
                      AND `c`.`revision` = {account.PasswordCredentialRevision};
                    """, cancellationToken).ConfigureAwait(false);

            expectedAffected = 1;
        }
        else
        {
            affected = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `accounts` AS `a`
                    INNER JOIN `account_password_credentials` AS `c`
                        ON `c`.`account_id` = `a`.`account_id`
                    SET `a`.`last_successful_login_at_utc` =
                            CASE
                                WHEN `a`.`last_successful_login_at_utc` IS NULL
                                    OR `a`.`last_successful_login_at_utc` < {successfulLoginAtUtc}
                                THEN {successfulLoginAtUtc}
                                ELSE `a`.`last_successful_login_at_utc`
                            END,
                        `c`.`password_hash` = {replacementPasswordHash},
                        `c`.`password_changed_at_utc` = {successfulLoginAtUtc},
                        `c`.`revision` = `c`.`revision` + 1
                    WHERE `a`.`account_id` = {account.AccountId}
                      AND `a`.`username` COLLATE utf8mb4_0900_bin =
                          {account.Username} COLLATE utf8mb4_0900_bin
                      AND `a`.`access_status` = {(byte)AccountAccessStatus.Active}
                      AND `a`.`deleted_at_utc` IS NULL
                      AND `a`.`state_revision` = {account.AccountStateRevision}
                      AND `c`.`password_hash` = {account.PasswordHash}
                      AND `c`.`revision` = {account.PasswordCredentialRevision};
                    """, cancellationToken).ConfigureAwait(false);

            expectedAffected = 2;
        }

        if (affected == 0)
        {
            return false;
        }

        if (affected != expectedAffected)
        {
            throw new InvalidOperationException($"Authentication persistence affected an unexpected number of rows. Expected {expectedAffected}, but MySQL reported {affected}.");
        }

        return true;
    }

    private static void ValidatePasswordHash(string hash, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash, parameterName);

        if (hash.Length > AccountPasswordCredentialConfiguration.MaximumPasswordHashLength)
        {
            throw new ArgumentException("A password hash cannot exceed 255 characters.", parameterName);
        }
    }
}
