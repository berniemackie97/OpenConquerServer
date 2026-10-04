using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterWeaponSkillSetProcessor(ICharacterWeaponSkillSetRepository repository)
{
    private readonly ICharacterWeaponSkillSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Takes ownership of <paramref name="awaitingWeaponSkillSet"/> and transfers its connection only after the native weapon-skill request is validated and the complete weapon-skill snapshot is sent.
    /// </summary>
    public async ValueTask<AwaitingMagicSetConnection> ProcessAsync(AwaitingWeaponSkillSetConnection awaitingWeaponSkillSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingWeaponSkillSet);

        ExistingCharacterGameConnection connection = awaitingWeaponSkillSet.TakeConnection();
        CharacterLoginProfile profile = awaitingWeaponSkillSet.Profile;
        GameMapEntryDefinition map = awaitingWeaponSkillSet.Map;
        CharacterItemSet itemSet = awaitingWeaponSkillSet.ItemSet;
        CharacterSocialRelationSet socialRelationSet = awaitingWeaponSkillSet.SocialRelationSet;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction10010 weaponSkillSetRequest;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the weapon-skill-set request.");
                }

                if (!GameAction10010.TryParse(frame, out weaponSkillSetRequest, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-friend-list GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || weaponSkillSetRequest.StringCount != 0)
                {
                    throw new InvalidDataException("The native weapon-skill-set request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (weaponSkillSetRequest.Action != GameAction10010.GetWeaponSkillSetAction)
            {
                throw new InvalidDataException($"Expected weapon-skill-set action {GameAction10010.GetWeaponSkillSetAction}, received action {weaponSkillSetRequest.Action}.");
            }

            if (weaponSkillSetRequest.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The weapon-skill-set request character ID does not match the authenticated character.");
            }

            if (weaponSkillSetRequest.ParameterPair != 0 || weaponSkillSetRequest.ActionParameter != 0 || weaponSkillSetRequest.Direction != 0
                || weaponSkillSetRequest.PositionX != 0 || weaponSkillSetRequest.PositionY != 0 || weaponSkillSetRequest.Data1 != 0
                || weaponSkillSetRequest.Data2 != 0 || weaponSkillSetRequest.Flag != 0)
            {
                throw new InvalidDataException("The native weapon-skill-set request requires all non-identity, non-timestamp and non-action MsgAction fields to be zero.");
            }

            CharacterWeaponSkillSet persistedWeaponSkillSet = await _repository.LoadAsync(profile.Identity.CharacterId, operationToken).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (persistedWeaponSkillSet.CharacterId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException($"The hydrated weapon-skill set belongs to character {persistedWeaponSkillSet.CharacterId}, not authenticated character {profile.Identity.CharacterId}.");
            }

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, parameterPair: 0, actionParameter: 0, weaponSkillSetRequest.Timestamp,
                GameAction10010.GetWeaponSkillSetAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

            foreach (WeaponSkill skill in persistedWeaponSkillSet.Skills)
            {
                GameWeaponSkillPacket1025 skillPacket = new(skill.Type, skill.Level, skill.Experience, skill.NextLevelExperienceRequirement);
                await connection.WriteAsync(skillPacket, operationToken).ConfigureAwait(false);
            }

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingMagicSetConnection(connection, map, itemSet, socialRelationSet, persistedWeaponSkillSet);
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

        return new AggregateException("Existing-character weapon-skill-set processing failed and connection cleanup also failed.", failures);
    }
}
