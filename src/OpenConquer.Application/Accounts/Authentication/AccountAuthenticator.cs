using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Implements authoritative account password authentication independently of
/// persistence, password-hashing technology, and authentication-attempt
/// protection implementation.
/// </summary>
public sealed class AccountAuthenticator(IAccountAuthenticationRepository repository, IAccountPasswordVerifier passwordVerifier, IAccountAuthenticationAttemptLimiter attemptLimiter )
    : IAccountAuthenticator
{
    private readonly IAccountAuthenticationRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IAccountPasswordVerifier _passwordVerifier = passwordVerifier ?? throw new ArgumentNullException(nameof(passwordVerifier));
    private readonly IAccountAuthenticationAttemptLimiter _attemptLimiter = attemptLimiter ?? throw new ArgumentNullException(nameof(attemptLimiter));

    public async ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(remoteAddress);

        cancellationToken.ThrowIfCancellationRequested();

        AccountAuthenticationSnapshot? account = await _repository.FindByNameAsync(accountName, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (account is null)
        {
            _passwordVerifier.VerifyDecoy(password.Span);

            cancellationToken.ThrowIfCancellationRequested();

            return AccountAuthenticationResult.InvalidCredentials();
        }

        if (!_attemptLimiter.TryBeginAuthentication(remoteAddress, account.AccountId, out IAccountAuthenticationAttemptLease? authenticationAttempt))
        {
            cancellationToken.ThrowIfCancellationRequested();

            return AccountAuthenticationResult.InvalidCredentials();
        }

        using (authenticationAttempt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AccountPasswordVerificationStatus verificationStatus = _passwordVerifier.VerifyPassword(account.PasswordHash, password.Span);

            cancellationToken.ThrowIfCancellationRequested();

            if (verificationStatus == AccountPasswordVerificationStatus.Failed)
            {
                authenticationAttempt.Complete(credentialsAccepted: false);

                return AccountAuthenticationResult.InvalidCredentials();
            }

            if (verificationStatus is not (AccountPasswordVerificationStatus.Success or AccountPasswordVerificationStatus.SuccessRehashNeeded))
            {
                throw new InvalidOperationException($"Password verifier returned unsupported status {verificationStatus}.");
            }

            switch (account.Access)
            {
                case AccountLoginAccess.Banned:
                    authenticationAttempt.Complete(credentialsAccepted: true);

                    return AccountAuthenticationResult.Banned();

                case AccountLoginAccess.Denied:
                    authenticationAttempt.Complete(credentialsAccepted: true);

                    return AccountAuthenticationResult.InvalidCredentials();

                case AccountLoginAccess.Allowed:
                    break;

                default:
                    throw new InvalidOperationException($"Account authentication snapshot contains unsupported login access {account.Access}.");
            }

            if (verificationStatus == AccountPasswordVerificationStatus.SuccessRehashNeeded)
            {
                string replacementPasswordHash = _passwordVerifier.HashPassword(password.Span);

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(replacementPasswordHash))
                {
                    throw new InvalidOperationException("Password verifier returned an invalid replacement hash.");
                }

                _ = await _repository.TryReplacePasswordHashAsync(account.AccountId, account.PasswordHash, replacementPasswordHash, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
            }

            authenticationAttempt.Complete(credentialsAccepted: true);

            return AccountAuthenticationResult.Succeeded(account.AccountId);
        }
    }
}
