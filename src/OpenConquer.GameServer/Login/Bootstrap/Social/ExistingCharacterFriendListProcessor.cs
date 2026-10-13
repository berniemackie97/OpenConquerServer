using OpenConquer.Application.Social.Hydration;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Social;

internal sealed class ExistingCharacterFriendListProcessor(ICharacterSocialRelationSetRepository repository, ICharacterPresenceReader presenceReader)
{
    private readonly ICharacterSocialRelationSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ICharacterPresenceReader _presenceReader = presenceReader ?? throw new ArgumentNullException(nameof(presenceReader));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAsync(connection, GameAction.GetFriendListAction, "friend-list request", cancellationToken).ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "friend-list request");

        CharacterSocialRelationSet persistedRelationSet = await _repository.LoadAsync(login.CharacterId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (persistedRelationSet.CharacterId != login.CharacterId)
        {
            throw new InvalidDataException($"The hydrated social-relation set belongs to character {persistedRelationSet.CharacterId}, not authenticated character {login.CharacterId}.");
        }

        ExistingCharacterFriendListWireProjection projection = ExistingCharacterFriendListWireProjection.Create(persistedRelationSet, _presenceReader);

        cancellationToken.ThrowIfCancellationRequested();

        GameActionPacket acknowledgement = new(login.CharacterId, parameterPair: 0, actionParameter: 0, request.Timestamp, GameAction.GetFriendListAction,
            direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

        foreach (GameSocialRelationPacket relationPacket in projection.RelationPackets)
        {
            await connection.WriteAsync(relationPacket, cancellationToken).ConfigureAwait(false);
        }

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.SocialRelationSet = projection.RuntimeRelationSet;
    }
}
