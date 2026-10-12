using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Creation;

/// <summary>
/// Fully resolved, server-authoritative initial character state ready for persistence.
/// </summary>
public sealed class CharacterCreationCandidate
{
    public const byte StartingLevel = 1;
    private const int StartingAttributeBudget = 10;

    public CharacterCreationCandidate(uint accountId, string name, ushort body, ushort profession, uint appearanceComposite,
        ushort hairComposite, ushort strength, ushort agility, ushort vitality, ushort spirit, ushort currentMana,
        uint mapId, ushort positionX, ushort positionY)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        if (!CharacterNamePolicy.IsValid(name))
        {
            throw new ArgumentException("The character name does not satisfy the server name policy.", nameof(name));
        }

        if (body is not (1003 or 1004 or 2001 or 2002))
        {
            throw new ArgumentOutOfRangeException(nameof(body), "Unsupported starting body.");
        }

        if (profession is not (10 or 20 or 40 or 50 or 60 or 100))
        {
            throw new ArgumentOutOfRangeException(nameof(profession), "Unsupported starting profession.");
        }

        if (appearanceComposite / 10_000 == 0 || appearanceComposite % 10_000 != body)
        {
            throw new ArgumentOutOfRangeException(nameof(appearanceComposite), "The appearance must contain the selected body.");
        }

        int allocatedPoints = strength + agility + vitality + spirit;

        if (allocatedPoints != StartingAttributeBudget)
        {
            throw new ArgumentException($"Level-one starting attributes must allocate exactly {StartingAttributeBudget} points.");
        }

        int maximumLife = (strength + agility + spirit + vitality * 8) * 3;

        if (maximumLife is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(vitality), "Starting attributes produce an invalid maximum life.");
        }

        if (mapId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        AccountId = accountId;
        Name = name;
        Body = body;
        Profession = checked((byte)profession);
        AppearanceComposite = appearanceComposite;
        HairComposite = hairComposite;
        Strength = strength;
        Agility = agility;
        Vitality = vitality;
        Spirit = spirit;
        CurrentLife = checked((ushort)maximumLife);
        CurrentMana = currentMana;
        MapId = mapId;
        PositionX = positionX;
        PositionY = positionY;
    }

    public uint AccountId { get; }
    public string Name { get; }
    public ushort Body { get; }
    public byte Profession { get; }
    public uint AppearanceComposite { get; }
    public ushort HairComposite { get; }
    public ushort Strength { get; }
    public ushort Agility { get; }
    public ushort Vitality { get; }
    public ushort Spirit { get; }
    public ushort CurrentLife { get; }
    public ushort CurrentMana { get; }
    public uint MapId { get; }
    public ushort PositionX { get; }
    public ushort PositionY { get; }
}
