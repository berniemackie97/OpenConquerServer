using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterSilentInfoReportProcessor
{
    /// <summary>
    /// Takes ownership of <paramref name="awaitingSilentInfoReport"/> and transfers its connection only after the native silent-info request is validated and acknowledged.
    /// </summary>
    public async ValueTask<AwaitingStatisticRequestConnection> ProcessAsync(AwaitingSilentInfoReportConnection awaitingSilentInfoReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingSilentInfoReport);

        ExistingCharacterGameConnection connection = awaitingSilentInfoReport.TakeConnection();
        CharacterLoginProfile profile = awaitingSilentInfoReport.Profile;
        GameMapEntryDefinition map = awaitingSilentInfoReport.Map;
        CharacterItemSet itemSet = awaitingSilentInfoReport.ItemSet;
        CharacterSocialRelationSet socialRelationSet = awaitingSilentInfoReport.SocialRelationSet;
        CharacterWeaponSkillSet weaponSkillSet = awaitingSilentInfoReport.WeaponSkillSet;
        CharacterMagicSet magicSet = awaitingSilentInfoReport.MagicSet;
        CharacterSyndicateState syndicateState = awaitingSilentInfoReport.SyndicateState;
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
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the silent-info report.");
                }

                if (!GameAction10010.TryParse(frame, out request, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-syndicate GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || request.StringCount != 0)
                {
                    throw new InvalidDataException("The native silent-info report must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (request.Action != GameAction10010.ReportSilentInfoAction)
            {
                throw new InvalidDataException($"Expected silent-info action {GameAction10010.ReportSilentInfoAction}, received action {request.Action}.");
            }

            if (request.EntityId != 0)
            {
                throw new InvalidDataException("The native silent-info report requires a zero entity ID.");
            }

            if (request.ActionParameter != 0 || request.Direction != 0 || request.PositionX != 0
                || request.PositionY != 0 || request.Data1 != 0 || request.Data2 != 0 || request.Flag != 0)
            {
                throw new InvalidDataException("The native silent-info report requires all fields other than its checksum, version, and action to be zero.");
            }

            operationToken.ThrowIfCancellationRequested();

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, request.ParameterPair,
                actionParameter: 0, request.Timestamp, GameAction10010.ReportSilentInfoAction, direction: 0,
                positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingStatisticRequestConnection(connection, map, itemSet, socialRelationSet, weaponSkillSet,
                magicSet, syndicateState, request.ParameterPair, request.Timestamp);
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

        return new AggregateException("Existing-character silent-info processing failed and connection cleanup also failed.", failures);
    }
}
