using System.Collections.Frozen;
using System.Security.Cryptography;

namespace OpenConquer.Application.Characters.Creation;

/// <summary>
/// Validated starting state shared by every character creation request.
/// </summary>
public sealed class CharacterCreationStartingState
{
    private const int RequiredFamilyCount = 6;
    private const uint MaleFace = 201;
    private const uint FemaleFace = 1;
    private const uint AppearanceScale = 10_000;

    private readonly FrozenDictionary<ushort, CharacterCreationStartingAllotment> _allotments;

    public CharacterCreationStartingState(uint mapId, ushort positionX, ushort positionY, IReadOnlyCollection<CharacterCreationStartingAllotment> allotments)
    {
        if (mapId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        ArgumentNullException.ThrowIfNull(allotments);

        if (allotments.Count != RequiredFamilyCount)
        {
            throw new ArgumentException($"Starting-state content must define exactly {RequiredFamilyCount} profession families.", nameof(allotments));
        }

        Dictionary<ushort, CharacterCreationStartingAllotment> rows = new(RequiredFamilyCount);

        foreach (CharacterCreationStartingAllotment allotment in allotments)
        {
            if (!IsCreatableFamily(allotment.Family))
            {
                throw new ArgumentException($"Unsupported starting profession family {allotment.Family}.", nameof(allotments));
            }

            if (allotment.Total != CharacterCreationCandidate.StartingAttributeBudget)
            {
                throw new ArgumentException($"Starting profession family {allotment.Family} allocates {allotment.Total} points instead of {CharacterCreationCandidate.StartingAttributeBudget}.", nameof(allotments));
            }

            if (!rows.TryAdd(allotment.Family, allotment))
            {
                throw new ArgumentException($"Duplicate starting profession family {allotment.Family}.", nameof(allotments));
            }
        }

        if (rows.Count != RequiredFamilyCount)
        {
            throw new ArgumentException("Starting-state content is incomplete.", nameof(allotments));
        }

        _allotments = rows.ToFrozenDictionary();
        MapId = mapId;
        PositionX = positionX;
        PositionY = positionY;
    }

    public uint MapId { get; }
    public ushort PositionX { get; }
    public ushort PositionY { get; }

    public CharacterCreationCandidate? CreateCandidate(uint accountId, string name, ushort body, ushort profession)
    {
        uint face = body switch
        {
            1003 or 1004 => MaleFace,
            2001 or 2002 => FemaleFace,
            _ => 0,
        };

        if (face == 0 || !IsCreatableProfession(profession))
        {
            return null;
        }

        ushort family = checked((ushort)(profession % 1000 / 10));
        CharacterCreationStartingAllotment allotment = _allotments[family];

        int hairColor = RandomNumberGenerator.GetInt32(3, 9);
        int hairStyle = RandomNumberGenerator.GetInt32(30, 56);

        ushort hairComposite = checked((ushort)(hairColor * 100 + hairStyle));
        uint appearanceComposite = checked(face * AppearanceScale + body);

        return new CharacterCreationCandidate(accountId, name, body, profession, appearanceComposite, hairComposite, allotment.Strength, allotment.Agility, allotment.Vitality, allotment.Spirit, currentMana: 0, MapId, PositionX, PositionY);
    }

    private static bool IsCreatableProfession(ushort profession)
    {
        return profession is 10 or 20 or 40 or 50 or 60 or 100;
    }

    private static bool IsCreatableFamily(ushort family)
    {
        return family is 1 or 2 or 4 or 5 or 6 or 10;
    }
}
