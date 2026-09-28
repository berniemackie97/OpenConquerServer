namespace OpenConquer.Infrastructure.Persistence.Game;

internal sealed class CharacterRecord
{
    public uint CharacterId { get; set; }
    public uint AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint AppearanceComposite { get; set; }
    public ushort HairComposite { get; set; }
    public byte Level { get; set; }
    public ulong Experience { get; set; }
    public ushort Strength { get; set; }
    public ushort Agility { get; set; }
    public ushort Vitality { get; set; }
    public ushort Spirit { get; set; }
    public ushort UnspentAttributePoints { get; set; }
    public ushort CurrentLife { get; set; }
    public ushort CurrentMana { get; set; }
    public byte Profession { get; set; }
    public byte FirstProfession { get; set; }
    public byte PreviousProfession { get; set; }
    public byte RebirthCount { get; set; }
    public byte PreRebirthLevel { get; set; }
    public uint Silver { get; set; }
    public uint ConquerPoints { get; set; }
    public uint BoundConquerPoints { get; set; }
    public short PkPoints { get; set; }
    public ushort TitleId { get; set; }
    public ushort EnlightenmentPoints { get; set; }
    public uint MapId { get; set; }
    public ushort PositionX { get; set; }
    public ushort PositionY { get; set; }
}
