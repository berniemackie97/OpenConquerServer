using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterFriendListProcessor(ICharacterSocialRelationSetRepository repository, ICharacterPresenceReader presenceReader)
{
    private readonly ICharacterSocialRelationSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ICharacterPresenceReader _presenceReader = presenceReader ?? throw new ArgumentNullException(nameof(presenceReader));

    /// <summary>
    /// Takes ownership of <paramref name="awaitingFriendList"/> and transfers its connection only after the native friend-list request is validated and the complete social-relation snapshot is sent.
    /// </summary>
    public async ValueTask<AwaitingWeaponSkillSetConnection> ProcessAsync(AwaitingFriendListConnection awaitingFriendList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingFriendList);

        ExistingCharacterGameConnection connection = awaitingFriendList.TakeConnection();
        CharacterLoginProfile profile = awaitingFriendList.Profile;
        GameMapEntryDefinition map = awaitingFriendList.Map;
        CharacterItemSet itemSet = awaitingFriendList.ItemSet;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction10010 friendListRequest;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the friend-list request.");
                }

                if (!GameAction10010.TryParse(frame, out friendListRequest, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-item-set GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || friendListRequest.StringCount != 0)
                {
                    throw new InvalidDataException("The native friend-list request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (friendListRequest.Action != GameAction10010.GetGoodFriendAction)
            {
                throw new InvalidDataException($"Expected friend-list action {GameAction10010.GetGoodFriendAction}, received action {friendListRequest.Action}.");
            }

            if (friendListRequest.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The friend-list request character ID does not match the authenticated character.");
            }

            if (friendListRequest.ParameterPair != 0 || friendListRequest.ActionParameter != 0 || friendListRequest.Direction != 0
                || friendListRequest.PositionX != 0 || friendListRequest.PositionY != 0 || friendListRequest.Data1 != 0
                || friendListRequest.Data2 != 0 || friendListRequest.Flag != 0)
            {
                throw new InvalidDataException("The native friend-list request requires all non-identity, non-timestamp and non-action MsgAction fields to be zero.");
            }

            CharacterSocialRelationSet persistedRelationSet = await _repository.LoadAsync(profile.Identity.CharacterId, operationToken).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (persistedRelationSet.CharacterId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException($"The hydrated social-relation set belongs to character {persistedRelationSet.CharacterId}, not authenticated character {profile.Identity.CharacterId}.");
            }

            ExistingCharacterFriendListWireProjection projection = ExistingCharacterFriendListWireProjection.Create(persistedRelationSet, _presenceReader);

            operationToken.ThrowIfCancellationRequested();

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, parameterPair: 0, actionParameter: 0, friendListRequest.Timestamp, GameAction10010.GetGoodFriendAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

            foreach (GameSocialRelationPacket1019 relationPacket in projection.RelationPackets)
            {
                await connection.WriteAsync(relationPacket, operationToken).ConfigureAwait(false);
            }

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingWeaponSkillSetConnection(connection, map, itemSet, projection.RuntimeRelationSet);
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

        return new AggregateException("Existing-character friend-list processing failed and connection cleanup also failed.", failures);
    }
}
