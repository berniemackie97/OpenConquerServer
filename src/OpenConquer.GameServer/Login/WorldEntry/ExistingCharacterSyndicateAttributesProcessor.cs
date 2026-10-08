using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterSyndicateAttributesProcessor(ICharacterSyndicateStateRepository repository, TimeProvider timeProvider)
{
    private readonly ICharacterSyndicateStateRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Takes ownership of <paramref name="awaitingSyndicateAttributes"/> and transfers its connection only after the native syndicate-attributes request is validated and the complete response is sent.
    /// </summary>
    public async ValueTask<AwaitingSilentInfoReportConnection> ProcessAsync(AwaitingSyndicateAttributesConnection awaitingSyndicateAttributes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingSyndicateAttributes);

        ExistingCharacterGameConnection connection = awaitingSyndicateAttributes.TakeConnection();
        CharacterLoginProfile profile = awaitingSyndicateAttributes.Profile;
        GameMapEntryDefinition map = awaitingSyndicateAttributes.Map;
        CharacterItemSet itemSet = awaitingSyndicateAttributes.ItemSet;
        CharacterSocialRelationSet socialRelationSet = awaitingSyndicateAttributes.SocialRelationSet;
        CharacterWeaponSkillSet weaponSkillSet = awaitingSyndicateAttributes.WeaponSkillSet;
        CharacterMagicSet magicSet = awaitingSyndicateAttributes.MagicSet;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction10010 request;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the syndicate-attributes request.");
                }

                if (!GameAction10010.TryParse(frame, out request, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-magic-set GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || request.StringCount != 0)
                {
                    throw new InvalidDataException("The native syndicate-attributes request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (request.Action != GameAction10010.GetSyndicateAttributesAction)
            {
                throw new InvalidDataException($"Expected syndicate-attributes action {GameAction10010.GetSyndicateAttributesAction}, received action {request.Action}.");
            }

            if (request.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The syndicate-attributes request character ID does not match the authenticated character.");
            }

            if (request.ParameterPair != 0 || request.ActionParameter != 0 || request.Direction != 0
                || request.PositionX != 0 || request.PositionY != 0 || request.Data1 != 0
                || request.Data2 != 0 || request.Flag != 0)
            {
                throw new InvalidDataException("The native syndicate-attributes request requires all non-identity, non-timestamp and non-action MsgAction fields to be zero.");
            }

            CharacterSyndicateState persistedState = await _repository.LoadAsync(profile.Identity.CharacterId, operationToken).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (persistedState is null)
            {
                throw new InvalidDataException("The syndicate-state repository returned a null state.");
            }

            if (persistedState.CharacterId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException($"The hydrated syndicate state belongs to character {persistedState.CharacterId}, not authenticated character {profile.Identity.CharacterId}.");
            }

            GameSyndicateAttributeInfoPacket1106? attributes = ExistingCharacterSyndicateAttributesWireProjection.Create(
                persistedState, _timeProvider.GetUtcNow());

            operationToken.ThrowIfCancellationRequested();

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, parameterPair: 0, actionParameter: 0,
                request.Timestamp, GameAction10010.GetSyndicateAttributesAction, direction: 0, positionX: 0, positionY: 0,
                data1: 0, data2: 0, flag: 0);

            if (attributes is not null)
            {
                await connection.WriteAsync(attributes, operationToken).ConfigureAwait(false);
            }

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingSilentInfoReportConnection(connection, map, itemSet, socialRelationSet, weaponSkillSet, magicSet, persistedState);
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

        return new AggregateException("Existing-character syndicate-attributes processing failed and connection cleanup also failed.", failures);
    }
}
