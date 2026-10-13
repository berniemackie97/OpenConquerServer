using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Syndicates;

internal sealed class ExistingCharacterSyndicateAttributesProcessor(ICharacterSyndicateStateRepository repository, TimeProvider timeProvider)
{
    private readonly ICharacterSyndicateStateRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAsync(connection, GameAction.GetSyndicateAttributesAction, "syndicate-attributes request", cancellationToken)
            .ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "syndicate-attributes request");

        CharacterSyndicateState persistedState = await _repository.LoadAsync(login.CharacterId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (persistedState is null)
        {
            throw new InvalidDataException("The syndicate-state repository returned a null state.");
        }

        if (persistedState.CharacterId != login.CharacterId)
        {
            throw new InvalidDataException($"The hydrated syndicate state belongs to character {persistedState.CharacterId}, not authenticated character {login.CharacterId}.");
        }

        GameSyndicateAttributesPacket? attributes = ExistingCharacterSyndicateAttributesWireProjection.Create(persistedState, _timeProvider.GetUtcNow());

        cancellationToken.ThrowIfCancellationRequested();

        GameActionPacket acknowledgement = new(login.CharacterId, parameterPair: 0, actionParameter: 0, request.Timestamp,
            GameAction.GetSyndicateAttributesAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

        if (attributes is not null)
        {
            await connection.WriteAsync(attributes, cancellationToken).ConfigureAwait(false);
        }

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.SyndicateState = persistedState;
    }
}
