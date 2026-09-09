using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Mutations;

public sealed class AccountMutationService(IAccountMutationStore store, IAccountPasswordHasher passwordHasher)
{
    private readonly IAccountMutationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IAccountPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));

    public ValueTask<AccountMutationStatus> ChangeAccessStatusAsync(uint accountId, AccountAccessStatus newAccessStatus, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);

        if (!Enum.IsDefined(newAccessStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(newAccessStatus), "The requested account access status is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _store.ChangeAccessStatusAsync(accountId, newAccessStatus, expectedStateRevision, context, cancellationToken);
    }

    public ValueTask<AccountMutationStatus> ChangeAuthorityRoleAsync(uint accountId, AccountAuthorityRole newAuthorityRole, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);

        if (!Enum.IsDefined(newAuthorityRole))
        {
            throw new ArgumentOutOfRangeException(nameof(newAuthorityRole), "The requested account authority role is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _store.ChangeAuthorityRoleAsync(accountId, newAuthorityRole, expectedStateRevision, context, cancellationToken);
    }

    public ValueTask<AccountMutationStatus> DeleteAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);
        cancellationToken.ThrowIfCancellationRequested();

        return _store.DeleteAccountAsync(accountId, expectedStateRevision, context, cancellationToken);
    }

    public ValueTask<AccountMutationStatus> RestoreAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);
        cancellationToken.ThrowIfCancellationRequested();

        return _store.RestoreAccountAsync(accountId, expectedStateRevision, context, cancellationToken);
    }

    public ValueTask<AccountMutationStatus> ResetPasswordAsync(uint accountId, ReadOnlyMemory<char> newPassword, ulong expectedStateRevision, ulong expectedPasswordCredentialRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);

        if (context.ActorKind != AccountActorKind.System)
        {
            throw new ArgumentException("Password reset requires a system actor.", nameof(context));
        }

        if (expectedPasswordCredentialRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPasswordCredentialRevision), "A password reset requires a valid password-credential revision.");
        }

        if (!AccountCredentialPolicy.IsValidPassword(newPassword.Span))
        {
            throw new ArgumentException("The new password does not satisfy account credential policy.", nameof(newPassword));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string newPasswordHash = _passwordHasher.HashPassword(newPassword.Span);

        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new InvalidOperationException("The account password hasher returned an invalid password hash.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _store.ResetPasswordAsync(accountId, newPasswordHash, expectedStateRevision, expectedPasswordCredentialRevision, context, cancellationToken);
    }

    private static void ValidateTarget(uint accountId, ulong expectedStateRevision, AccountMutationContext context)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An account mutation requires a persisted account.");
        }

        if (expectedStateRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStateRevision), "An account mutation requires a valid account-state revision.");
        }

        ArgumentNullException.ThrowIfNull(context);
    }
}
