using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Hosting;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Character;

// Owns the authenticated game connection until the existing-character handoff.
// Waiting at the creation screen never occupies a fixed login worker.
internal sealed class GameCharacterCreationLoginProcessor : IDisposable
{
    private static readonly TimeSpan s_creationDeadline = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_repeatProofDeadline = TimeSpan.FromSeconds(30);
    private const int MaximumCreationAttempts = 8;

    private readonly CharacterCreationService _creation;
    private readonly ICharacterLoginResolver _resolver;
    private readonly GameExistingCharacterLoginProcessor _existingCharacter;
    private readonly SemaphoreSlim _slots;

    public GameCharacterCreationLoginProcessor(CharacterCreationService creation, ICharacterLoginResolver resolver,
        GameExistingCharacterLoginProcessor existingCharacter, GameNetworkOptions network)
    {
        _creation = creation ?? throw new ArgumentNullException(nameof(creation));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _existingCharacter = existingCharacter ?? throw new ArgumentNullException(nameof(existingCharacter));
        ArgumentNullException.ThrowIfNull(network);
        _slots = new SemaphoreSlim(Math.Min(network.AdmissionCapacity, 1024));
    }

    public void Dispose() => _slots.Dispose();

    // The caller transfers ownership even when capacity is exhausted.
    public async Task<WorldSession?> ProcessAsync(AuthenticatedGameConnection connection, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!_slots.Wait(0))
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                throw new GameCharacterCreationCleanupException(failure);
            }
            return null;
        }

        AuthenticatedGameConnection? owned = connection;
        Exception? processingFailure = null;
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(s_creationDeadline);
            CancellationToken operation = deadline.Token;

            await owned.WriteAsync(GameCharacterCreationControlPackets.NewRole, operation).ConfigureAwait(false);

            uint createdCharacterId = 0;
            for (int attempt = 0; attempt < MaximumCreationAttempts; attempt++)
            {
                GameCharacterCreationRequest request;
                using (GameInboundFrame? frame = await owned.ReadAsync(operation).ConfigureAwait(false))
                {
                    if (frame is null || !GameCharacterCreationRequest.TryParse(frame, out request, out _) ||
                        request.ClientReportedSessionUid != owned.SessionUid || request.CancelRequested)
                    {
                        return null;
                    }
                }

                // Only the redeemed ticket's account identity is trusted.
                CharacterCreationResult result = await _creation.CreateAsync(owned.AccountId,
                    request.Name, request.Body, request.Profession, operation).ConfigureAwait(false);

                if (result.Status == CharacterCreationStatus.Created)
                {
                    createdCharacterId = result.CharacterId;
                    break;
                }

                if (result.Status == CharacterCreationStatus.AccountAlreadyHasCharacter)
                {
                    return null;
                }

                await owned.WriteAsync(GameCharacterCreationControlPackets.Rejected(result.Status), operation)
                    .ConfigureAwait(false);
            }

            if (createdCharacterId == 0)
            {
                return null;
            }

            await owned.WriteAsync(GameCharacterCreationControlPackets.Accepted, operation).ConfigureAwait(false);
            deadline.CancelAfter(s_repeatProofDeadline);
            using (GameInboundFrame? frame = await owned.ReadAsync(operation).ConfigureAwait(false))
            {
                if (frame is null || !GameLoginProof.TryParse(frame, out GameLoginProof proof, out _) ||
                    !owned.TryConsumeRepeatedLoginProof(proof))
                {
                    throw new InvalidDataException("Character creation was followed by an invalid or missing game-session proof.");
                }
            }

            owned.DiscardRepeatedLoginProof();
            CharacterLoginResolution resolved = await _resolver.ResolveAsync(owned.AccountId, operation).ConfigureAwait(false);
            CharacterLoginProfile? profile = resolved.Profile;
            if (resolved.Route != CharacterLoginRoute.ExistingCharacter || profile is null ||
                profile.Identity.CharacterId != createdCharacterId ||
                profile.Identity.AccountId != owned.AccountId)
            {
                throw new InvalidDataException("Created character could not be resolved to the authenticated account.");
            }

            // The existing bootstrap takes sole ownership of the connection and every
            // subsequent account lease, writer fence, world admission and cleanup.
            await using CharacterLoginHandoffResult handoff = new(owned, resolved);
            owned = null;
            return await _existingCharacter.ProcessAsync(handoff, operation).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            processingFailure = failure;
            throw;
        }
        finally
        {
            try
            {
                if (owned is not null)
                {
                    await owned.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception cleanupFailure)
            {
                Exception cause = processingFailure is null ? cleanupFailure :
                    new AggregateException("Character creation and transport disposal both failed.",
                        processingFailure, cleanupFailure);
                throw new GameCharacterCreationCleanupException(cause);
            }
            finally
            {
                _slots.Release();
            }
        }
    }
}

internal sealed class GameCharacterCreationCleanupException(Exception cause) :
    Exception("Character creation transport cleanup failed; the realm must stop.", cause)
{
}
