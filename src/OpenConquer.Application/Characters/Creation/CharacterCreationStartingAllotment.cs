namespace OpenConquer.Application.Characters.Creation;

public readonly record struct CharacterCreationStartingAllotment(ushort Family, ushort Strength, ushort Agility, ushort Vitality, ushort Spirit)
{
    public uint Total => (uint)Strength + Agility + Vitality + Spirit;
}
