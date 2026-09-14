namespace OpenConquer.Application.Characters.Login;

public sealed class CharacterAppearance
{
    public CharacterAppearance(uint composite, ushort hair)
    {
        if (composite == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(composite), "A persisted character requires a nonzero appearance.");
        }

        Composite = composite;
        Hair = hair;
    }

    public uint Composite { get; }
    public ushort Hair { get; }
}
