namespace OpenConquer.Application.Characters.Login;

public readonly record struct CharacterEconomy
{
    public CharacterEconomy(uint silver, uint conquerPoints, uint boundConquerPoints)
    {
        Silver = silver;
        ConquerPoints = conquerPoints;
        BoundConquerPoints = boundConquerPoints;
    }

    public uint Silver { get; }
    public uint ConquerPoints { get; }
    public uint BoundConquerPoints { get; }
}
