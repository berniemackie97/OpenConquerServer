using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterMagicSetProcessor(ICharacterMagicSetRepository repository)
{
    private readonly ICharacterMagicSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Takes ownership of <paramref name="awaitingMagicSet"/> and transfers its connection only after the native magic-set request is validated and the complete magic snapshot is sent.
    /// </summary>
    public async ValueTask<AwaitingSyndicateAttributesConnection> ProcessAsync(AwaitingMagicSetConnection awaitingMagicSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingMagicSet);

        ExistingCharacterGameConnection connection = awaitingMagicSet.TakeConnection();
        CharacterLoginProfile profile = awaitingMagicSet.Profile;
        GameMapEntryDefinition map = awaitingMagicSet.Map;
        CharacterItemSet itemSet = awaitingMagicSet.ItemSet;
        CharacterSocialRelationSet socialRelationSet = awaitingMagicSet.SocialRelationSet;
        CharacterWeaponSkillSet weaponSkillSet = awaitingMagicSet.WeaponSkillSet;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction10010 magicSetRequest;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the magic-set request.");
                }

                if (!GameAction10010.TryParse(frame, out magicSetRequest, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-weapon-skill GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || magicSetRequest.StringCount != 0)
                {
                    throw new InvalidDataException("The native magic-set request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (magicSetRequest.Action != GameAction10010.GetMagicSetAction)
            {
                throw new InvalidDataException($"Expected magic-set action {GameAction10010.GetMagicSetAction}, received action {magicSetRequest.Action}.");
            }

            if (magicSetRequest.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The magic-set request character ID does not match the authenticated character.");
            }

            if (magicSetRequest.ParameterPair != 0 || magicSetRequest.ActionParameter != 0 || magicSetRequest.Direction != 0
                || magicSetRequest.PositionX != 0 || magicSetRequest.PositionY != 0 || magicSetRequest.Data1 != 0
                || magicSetRequest.Data2 != 0 || magicSetRequest.Flag != 0)
            {
                throw new InvalidDataException("The native magic-set request requires all non-identity, non-timestamp and non-action MsgAction fields to be zero.");
            }

            CharacterMagicSet persistedMagicSet = await _repository.LoadAsync(profile.Identity.CharacterId, operationToken).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (persistedMagicSet.CharacterId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException($"The hydrated magic set belongs to character {persistedMagicSet.CharacterId}, not authenticated character {profile.Identity.CharacterId}.");
            }

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, parameterPair: 0, actionParameter: 0, magicSetRequest.Timestamp,
                GameAction10010.GetMagicSetAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

            foreach (CharacterMagic magic in persistedMagicSet.Magic)
            {
                await connection.WriteAsync(new GameMagicEffectSimplePacket1103(profile.Identity.CharacterId, magic.Type, magic.Level), operationToken).ConfigureAwait(false);

                if (magic.Experience != 0)
                {
                    await connection.WriteAsync(new GameFlushExperiencePacket1104(magic.Experience, nextLevelExperienceRequirement: 0,
                        magic.Type, GameFlushExperiencePacket1104.MagicAction), operationToken).ConfigureAwait(false);
                }
            }

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingSyndicateAttributesConnection(connection, map, itemSet, socialRelationSet, weaponSkillSet, persistedMagicSet);
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

        return new AggregateException("Existing-character magic-set processing failed and connection cleanup also failed.", failures);
    }
}
