using System.Net;
using OpenConquer.Application.Accounts.Authentication.Passwords;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Registration;

/// <summary>
/// Creates self-service player accounts for the OpenConquer registration extension.
/// </summary>
/// <remarks>
/// Duplicate names are detected only by the unique index during the insert, so two concurrent requests for the
/// same name cannot both succeed and no separate existence query races the write.
/// </remarks>
public sealed class AccountRegistrationService(IAccountRegistrationStore store, IAccountPasswordHasher passwordHasher,
    IAccountRegistrationLimiter limiter)
{
    private readonly IAccountRegistrationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IAccountPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    private readonly IAccountRegistrationLimiter _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));

    public async ValueTask<AccountRegistrationResult> RegisterAsync(string username, ReadOnlyMemory<char> password,
        IPAddress remoteAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);
        cancellationToken.ThrowIfCancellationRequested();

        // Policy is checked before the quota so a rejected form does not use up a source's registrations.
        if (!AccountRegistrationPolicy.IsValidUsername(username) || !AccountRegistrationPolicy.IsValidPassword(password.Span))
        {
            return AccountRegistrationResult.Refused(AccountRegistrationStatus.InvalidRequest);
        }

        if (!_limiter.TryBeginRegistration(remoteAddress, out IDisposable? lease))
        {
            return AccountRegistrationResult.Refused(AccountRegistrationStatus.Throttled);
        }

        using (lease)
        {
            string passwordHash = _passwordHasher.HashPassword(password.Span);

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new InvalidOperationException("The account password hasher returned an invalid password hash.");
            }

            uint? accountId = await _store.TryCreateAsync(username, passwordHash, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

            return accountId is uint created
                ? AccountRegistrationResult.Created(created)
                : AccountRegistrationResult.Refused(AccountRegistrationStatus.UsernameTaken);
        }
    }
}
