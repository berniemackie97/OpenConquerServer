using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Admission;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Registers realm presence, records the account as playing in this realm, sends the existing-character bootstrap packets,
/// and starts the login context for the client requests.
/// </summary>
/// <remarks>
/// Presence is registered before the account record is written, so that a kick request from another realm always finds
/// the login it is aimed at; a record without presence is then known to be left over and is removed.
/// </remarks>
internal sealed class ExistingCharacterBootstrapProcessor(ICharacterPresenceRegistrar presenceRegistrar, RealmAccountSessions accountSessions, WorldMapContentCatalog maps,
    GameLoginMetrics metrics)
{
    private readonly ICharacterPresenceRegistrar _presenceRegistrar = presenceRegistrar ?? throw new ArgumentNullException(nameof(presenceRegistrar));
    private readonly RealmAccountSessions _accountSessions = accountSessions ?? throw new ArgumentNullException(nameof(accountSessions));
    private readonly WorldMapContentCatalog _maps = maps ?? throw new ArgumentNullException(nameof(maps));
    private readonly GameLoginMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <summary>
    /// Takes the handoff connection. If map resolution, bootstrap output, or presence registration fails, the connection is closed here.
    /// Returns <see langword="null"/> after refusing a login whose account is still playing in another realm; that realm has then been
    /// asked to disconnect it, as TQ's AccountServer did, so that the player's next attempt gets in.
    /// </summary>
    public async ValueTask<ExistingCharacterLoginContext?> ProcessAsync(CharacterLoginHandoffResult handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        AuthenticatedGameConnection connection = handoff.TakeConnection();
        AccountSessionLease? accountSession = null;
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

            presenceLease = _presenceRegistrar.Register(profile.Identity.CharacterId);
            accountSession = await _accountSessions.TryEnterAsync(connection.AccountId, profile.Identity.CharacterId, connection.SessionUid, cancellationToken).ConfigureAwait(false);

            if (accountSession is null)
            {
                presenceLease.Dispose();
                presenceLease = null;
                await connection.WriteAsync(GameLoginRefusals.AlreadyOnline, cancellationToken).ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
                _metrics.RecordAccountOnlineRefused();
                return null;
            }

            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.Accepted, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.LoginHistory, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(ExistingCharacterBootstrapPacketFactory.ServerState, cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(userInfo, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            characterConnection = new ExistingCharacterGameConnection(connection, profile, presenceLease, accountSession);
            presenceLease = null;
            accountSession = null;

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
                    finally
                    {
                        if (accountSession is not null)
                        {
                            await accountSession.DisposeAsync().ConfigureAwait(false);
                        }
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
