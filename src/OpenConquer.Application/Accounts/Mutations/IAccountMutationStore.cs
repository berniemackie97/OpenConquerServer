using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Mutations;

public interface IAccountMutationStore
{
    ValueTask<AccountMutationStatus> ChangeAccessStatusAsync(uint accountId, AccountAccessStatus newAccessStatus, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default);
    ValueTask<AccountMutationStatus> ChangeAuthorityRoleAsync(uint accountId, AccountAuthorityRole newAuthorityRole, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default);
    ValueTask<AccountMutationStatus> DeleteAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default);
    ValueTask<AccountMutationStatus> RestoreAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default);
    ValueTask<AccountMutationStatus> ResetPasswordAsync(uint accountId, string newPasswordHash, ulong expectedStateRevision, ulong expectedPasswordCredentialRevision, AccountMutationContext context, CancellationToken cancellationToken = default);
}
