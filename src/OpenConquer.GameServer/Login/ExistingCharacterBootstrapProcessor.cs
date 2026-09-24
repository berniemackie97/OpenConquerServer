using OpenConquer.Application.Characters.Login;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Sends the verified native 5517 existing character bootstrap sequence and transfers the live connection to the EnterMap stage.
/// </summary>
internal sealed class ExistingCharacterBootstrapProcessor
{
    /// <summary>
    /// Takes ownership of <paramref name="handoff"/> and transfers its connection only after the complete bootstrap sequence is written successfully.
    /// </summary>
    public async ValueTask<AwaitingEnterMapConnection> ProcessAsync(CharacterLoginHandoffResult handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        AuthenticatedGameConnection connection = handoff.Connection;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (handoff.Resolution.Route != CharacterLoginRoute.ExistingCharacter)
            {
                throw new ArgumentException("Existing-character bootstrap requires an existing-character login route.", nameof(handoff));
            }

            CharacterLoginProfile profile = handoff.Resolution.Profile
                                            ?? throw new ArgumentException("Existing-character bootstrap requires a resolved character profile.", nameof(handoff));

            GameUserInfoPacket1006 userInfo = ExistingCharacterBootstrapPacketFactory.CreateUserInfo(profile);
            AwaitingEnterMapConnection awaitingEnterMap = new(connection, profile);

            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.Accepted, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.LoginHistory, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.ServerState, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(userInfo, cancellationToken).ConfigureAwait(false);

            return awaitingEnterMap;
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

        return new AggregateException("Existing-character bootstrap failed and connection cleanup also failed.", failures);
    }
}
