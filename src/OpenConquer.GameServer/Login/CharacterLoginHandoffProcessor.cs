using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Resolution;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Resolves an authenticated GameServer connection to its persisted character-login route.
/// </summary>
internal sealed class CharacterLoginHandoffProcessor(ICharacterLoginResolver resolver)
{
    private readonly ICharacterLoginResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <summary>
    /// Takes ownership of the authenticated connection held by <paramref name="authentication"/> and transfers it to the returned handoff result only after character-login resolution succeeds.
    /// </summary>
    public ValueTask<CharacterLoginHandoffResult> ProcessAsync(GameConnectionAuthenticationResult authentication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        return ProcessOwnedConnectionAsync(authentication.TakeConnection(), cancellationToken);
    }

    private async ValueTask<CharacterLoginHandoffResult> ProcessOwnedConnectionAsync(AuthenticatedGameConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            CharacterLoginResolution resolution = await _resolver.ResolveAsync(connection.AccountId, cancellationToken).ConfigureAwait(false);
            return new CharacterLoginHandoffResult(connection, resolution);
        }
        catch (Exception processingException)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw CreateProcessingFailure(processingException, cleanupException);
            }

            throw;
        }
    }

    private static AggregateException CreateProcessingFailure(Exception processingException, Exception cleanupException)
    {
        List<Exception> failures = [processingException];

        if (cleanupException is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupException);
        }

        return new AggregateException("GameServer character-login handoff failed and connection cleanup also failed.", failures);
    }
}
