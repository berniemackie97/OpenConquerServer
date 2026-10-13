using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Skills;

internal sealed class ExistingCharacterMagicSetProcessor(ICharacterMagicSetRepository repository)
{
    private readonly ICharacterMagicSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAsync(connection, GameAction.GetMagicSetAction, "magic-set request", cancellationToken).ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "magic-set request");

        CharacterMagicSet persistedMagicSet = await _repository.LoadAsync(login.CharacterId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (persistedMagicSet.CharacterId != login.CharacterId)
        {
            throw new InvalidDataException($"The hydrated magic set belongs to character {persistedMagicSet.CharacterId}, not authenticated character {login.CharacterId}.");
        }

        GameActionPacket acknowledgement = new(login.CharacterId, parameterPair: 0, actionParameter: 0, request.Timestamp,
            GameAction.GetMagicSetAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

        foreach (CharacterMagic magic in persistedMagicSet.Magic)
        {
            await connection.WriteAsync(new GameMagicPacket(login.CharacterId, magic.Type, magic.Level), cancellationToken).ConfigureAwait(false);

            if (magic.Experience != 0)
            {
                await connection.WriteAsync(new GameSkillExperiencePacket(magic.Experience, nextLevelExperienceRequirement: 0,
                    magic.Type, GameSkillExperiencePacket.MagicAction), cancellationToken).ConfigureAwait(false);
            }
        }

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.MagicSet = persistedMagicSet;
    }
}
