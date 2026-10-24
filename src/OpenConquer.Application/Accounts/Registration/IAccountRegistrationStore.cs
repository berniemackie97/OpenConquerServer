namespace OpenConquer.Application.Accounts.Registration;

public interface IAccountRegistrationStore
{
    /// <summary>
    /// Atomically creates an active player account, its password credential and its creation audit event.
    /// </summary>
    /// <returns>The new account ID, or <see langword="null"/> when the username is already taken.</returns>
    /// <remarks>
    /// An insert whose commit outcome is lost is settled by reading back <paramref name="creationOperationId"/>;
    /// it is never retried. Only a positive match confirms creation; a missing row or unavailable read leaves
    /// the outcome unknown and throws. Cancellation before commit must roll back the complete transaction.
    /// </remarks>
    ValueTask<uint?> TryCreateAsync(string username, string passwordHash, Guid creationOperationId,
        CancellationToken cancellationToken = default);
}
