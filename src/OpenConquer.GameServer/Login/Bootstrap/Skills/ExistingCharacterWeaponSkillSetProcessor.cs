using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Skills;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Skills;

internal sealed class ExistingCharacterWeaponSkillSetProcessor(ICharacterWeaponSkillSetRepository repository)
{
    private readonly ICharacterWeaponSkillSetRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAsync(connection, GameAction.GetWeaponSkillSetAction, "weapon-skill-set request", cancellationToken)
            .ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "weapon-skill-set request");

        CharacterWeaponSkillSet persistedWeaponSkillSet = await _repository.LoadAsync(login.CharacterId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (persistedWeaponSkillSet.CharacterId != login.CharacterId)
        {
            throw new InvalidDataException($"The hydrated weapon-skill set belongs to character {persistedWeaponSkillSet.CharacterId}, not authenticated character {login.CharacterId}.");
        }

        GameActionPacket acknowledgement = new(login.CharacterId, parameterPair: 0, actionParameter: 0, request.Timestamp,
            GameAction.GetWeaponSkillSetAction, direction: 0, positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

        foreach (WeaponSkill skill in persistedWeaponSkillSet.Skills)
        {
            GameWeaponSkillPacket skillPacket = new(skill.Type, skill.Level, skill.Experience, skill.NextLevelExperienceRequirement);
            await connection.WriteAsync(skillPacket, cancellationToken).ConfigureAwait(false);
        }

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.WeaponSkillSet = persistedWeaponSkillSet;
    }
}
