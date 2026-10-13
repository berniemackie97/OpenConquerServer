using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Sends the existing-character bootstrap packets, registers realm presence, and starts the login context for the client requests.
/// </summary>
internal sealed class ExistingCharacterBootstrapProcessor(ICharacterPresenceRegistrar presenceRegistrar, WorldMapContentCatalog maps)
{
    private readonly ICharacterPresenceRegistrar _presenceRegistrar = presenceRegistrar ?? throw new ArgumentNullException(nameof(presenceRegistrar));
    private readonly WorldMapContentCatalog _maps = maps ?? throw new ArgumentNullException(nameof(maps));

    /// <summary>
    /// Takes the handoff connection. If map resolution, bootstrap output, or presence registration fails, the connection is closed here.
    /// </summary>
    public async ValueTask<ExistingCharacterLoginContext> ProcessAsync(CharacterLoginHandoffResult handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        AuthenticatedGameConnection connection = handoff.TakeConnection();
        ICharacterPresenceLease? presenceLease = null;
        ExistingCharacterGameConnection? characterConnection = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (handoff.Resolution.Route != CharacterLoginRoute.ExistingCharacter)
            {
                throw new ArgumentException("Existing-character bootstrap requires an existing-character login route.", nameof(handoff));
            }

            CharacterLoginProfile profile = handoff.Resolution.Profile ?? throw new ArgumentException("Existing-character bootstrap requires a resolved character profile.", nameof(handoff));

            if (!_maps.TryGet(profile.Location.MapId, out WorldMapContent? map))
            {
                throw new InvalidDataException($"Character {profile.Identity.CharacterId} references unavailable world map {profile.Location.MapId}.");
            }

            GameCharacterInfoPacket userInfo = ExistingCharacterBootstrapPacketFactory.CreateCharacterInfo(profile);

            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.Accepted, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.LoginHistory, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.ServerState, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(userInfo, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            presenceLease = _presenceRegistrar.Register(profile.Identity.CharacterId);
            characterConnection = new ExistingCharacterGameConnection(connection, profile, presenceLease);
            presenceLease = null;

            return new ExistingCharacterLoginContext(characterConnection, map);
        }
        catch (Exception processingException)
        {
            try
            {
                if (characterConnection is not null)
                {
                    await characterConnection.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    Exception? presenceFailure = null;

                    try
                    {
                        presenceLease?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        presenceFailure = exception;
                    }

                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception connectionFailure)
                    {
                        if (presenceFailure is not null)
                        {
                            throw new AggregateException("Existing-character presence release and connection cleanup both failed.", presenceFailure, connectionFailure);
                        }

                        throw;
                    }

                    if (presenceFailure is not null)
                    {
                        throw presenceFailure;
                    }
                }
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

        return new AggregateException("Existing-character bootstrap failed and cleanup also failed.", failures);
    }
}
