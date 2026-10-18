using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// 5517 MsgPlayerSpawn (10014). Spawns another player, never the local hero. Field offsets are checked against
/// CMsgPlayerSpawn__Process (0x755F64).
/// </summary>
public sealed class GamePlayerSpawnPacket : IPacket
{
    private const int FixedPayloadLength = 214;
    private const byte StringEntryCount = 4;
    private readonly int _encodedNameLength;

    public GamePlayerSpawnPacket(GamePlayerSpawnSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.EntityId < 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(player), "The native player-spawn path requires a player entity ID.");
        }
        if (player.Direction >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(player), "An eight-direction spawn requires direction 0 through 7.");
        }
        if (player.Action > ushort.MaxValue ||
            (!GameAvatarAction.IsSupportedPose((ushort)player.Action) &&
             !(player.Action == 0x14B &&
               (player.StatusMaskLow & (1UL << 5)) != 0 &&
               (player.StatusMaskLow & (1UL << 10)) == 0)))
        {
            throw new ArgumentOutOfRangeException(nameof(player), "A player spawn requires a native 5517 action type.");
        }
        _encodedNameLength = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetByteCount(player.Name);
        if (_encodedNameLength is < 1 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(player), "Native player names must encode to 1 through 15 bytes.");
        }
        Player = player;
    }

    public GamePlayerSpawnSnapshot Player { get; }
    public ushort PacketId => 10014;
    public int PayloadLength => FixedPayloadLength + 1 + StringEntryCount + _encodedNameLength;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;
        GamePlayerSpawnSnapshot p = Player;
        writer.WriteUInt32(p.Appearance);
        writer.WriteUInt32(p.EntityId);
        writer.WriteUInt32(p.SyndicateId);
        writer.WriteUInt32(0); // Syndicate rank is deliberately unmodelled for spawn scaling.
        // The native status mask starts at frame +0x16, inside this original reserved area.
        writer.Reserve(2);
        writer.WriteUInt64(p.StatusMaskLow);
        writer.WriteUInt64(p.StatusMaskHigh);
        writer.WriteUInt16(0); // No transform look.
        writer.WriteUInt32(p.HeadwearItemType);
        writer.Reserve(4);
        writer.WriteUInt32(p.ArmorItemType);
        writer.WriteUInt32(p.LeftHandItemType);
        writer.WriteUInt32(p.RightHandItemType);
        writer.Reserve(20);
        writer.WriteUInt32(p.Life);
        writer.WriteUInt16(p.Hair);
        writer.WriteUInt16(p.X);
        writer.WriteUInt16(p.Y);
        writer.WriteByte(p.Direction);
        writer.WriteUInt32(p.Action); // Native 5517 action at frame +0x5B.
        writer.Reserve(3);
        writer.WriteByte(p.RebirthCount);
        writer.WriteUInt16(p.Level);
        writer.Reserve(66);
        writer.WriteUInt16(p.TitleId);
        writer.Reserve(13);
        writer.WriteUInt32(p.HeadwearItemType);
        writer.WriteUInt32(p.ArmorItemType);
        writer.WriteUInt32(p.LeftHandItemType);
        writer.WriteUInt32(p.RightHandItemType);
        writer.Reserve(9);
        writer.WriteUInt16(p.FirstProfession);
        writer.WriteUInt16(p.PreviousProfession);
        writer.WriteUInt16(p.Profession);
        writer.Reserve(5); // Idle client branch; never suppress position assignment.

        if (writer.Written - start != FixedPayloadLength)
        {
            throw new InvalidOperationException("Native player-spawn fixed body length changed.");
        }

        writer.WriteByte(StringEntryCount);
        writer.WriteByteString(p.Name, WireTextEncoding.Ansi);
        writer.WriteByteString(string.Empty);
        writer.WriteByteString(string.Empty);
        writer.WriteByteString(string.Empty);
    }
}

/// <summary>
/// What the spawn packet shows about another player. No account or inventory details.
/// </summary>
public sealed record GamePlayerSpawnSnapshot(
    uint EntityId, string Name, uint Appearance, ushort Hair, ushort X, ushort Y, byte Direction,
    uint Life, ushort Level, byte RebirthCount, ushort FirstProfession, ushort PreviousProfession,
    ushort Profession, ushort TitleId, uint SyndicateId, uint HeadwearItemType,
    uint ArmorItemType, uint LeftHandItemType, uint RightHandItemType, uint Action = 100,
    ulong StatusMaskLow = 0, ulong StatusMaskHigh = 0);
