namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

internal sealed class MagicRecord
{
    public uint OwnerCharacterId { get; set; }
    public ushort MagicType { get; set; }
    public ushort Level { get; set; }
    public uint Experience { get; set; }
}
