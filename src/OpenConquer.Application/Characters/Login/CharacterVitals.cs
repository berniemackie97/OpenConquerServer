namespace OpenConquer.Application.Characters.Login;

public readonly record struct CharacterVitals
{
    public CharacterVitals(ushort life, ushort mana)
    {
        Life = life;
        Mana = mana;
    }

    public ushort Life { get; }
    public ushort Mana { get; }
}
