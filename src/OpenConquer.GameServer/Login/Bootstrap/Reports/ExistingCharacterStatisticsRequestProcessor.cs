using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Reports;

internal sealed class ExistingCharacterStatisticsRequestProcessor
{
    /// <summary>
    /// Takes ownership of the awaiting connection and completes the bootstrap request sequence. The request has no response.
    /// </summary>
    public async ValueTask<ExistingCharacterBootstrapCompleteConnection> ProcessAsync(AwaitingStatisticsRequestConnection awaitingStatisticRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingStatisticRequest);

        ExistingCharacterGameConnection connection = awaitingStatisticRequest.TakeConnection();
        CharacterLoginProfile profile = awaitingStatisticRequest.Profile;
        WorldMapContent map = awaitingStatisticRequest.Map;
        CharacterItemSet itemSet = awaitingStatisticRequest.ItemSet;
        CharacterSocialRelationSet socialRelationSet = awaitingStatisticRequest.SocialRelationSet;
        CharacterWeaponSkillSet weaponSkillSet = awaitingStatisticRequest.WeaponSkillSet;
        CharacterMagicSet magicSet = awaitingStatisticRequest.MagicSet;
        CharacterSyndicateState syndicateState = awaitingStatisticRequest.SyndicateState;
        uint clientReportedSilentDataChecksum = awaitingStatisticRequest.ClientReportedSilentDataChecksum;
        uint clientReportedSilentDataVersion = awaitingStatisticRequest.ClientReportedSilentDataVersion;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction request;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the statistics request.");
                }

                if (!GameAction.TryParse(frame, out request, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-silent-info GameServer frame is not a valid action packet: {parseError}.");
                }

                if (frame.Header.Length != GameAction.FixedPacketLength || request.StringCount != 0)
                {
                    throw new InvalidDataException("The statistics request must contain the fixed action body without trailing strings.");
                }
            }

            if (request.Action != GameAction.GetStatisticAction)
            {
                throw new InvalidDataException($"Expected statistics action {GameAction.GetStatisticAction}, received action {request.Action}.");
            }

            if (request.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The statistics request character ID does not match the authenticated character.");
            }

            if (request.ParameterPair != 0 || request.ActionParameter != 0 || request.Direction != 0
                || request.PositionX != 0 || request.PositionY != 0 || request.Data1 != 0
                || request.Data2 != 0 || request.Flag != 0)
            {
                throw new InvalidDataException("The statistics request requires all non-identity, non-timestamp and non-action fields to be zero.");
            }

            operationToken.ThrowIfCancellationRequested();

            return new ExistingCharacterBootstrapCompleteConnection(connection, map, itemSet, socialRelationSet, weaponSkillSet,
                magicSet, syndicateState, clientReportedSilentDataChecksum, clientReportedSilentDataVersion);
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

        return new AggregateException("Existing-character statistics-request processing failed and connection cleanup also failed.", failures);
    }
}
