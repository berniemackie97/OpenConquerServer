using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Server-to-client account login history and location alert (packet 2078).
/// </summary>
public sealed class GameLoginHistoryPacket : IPacket
{
    public const ushort PacketIdentifier = 2078;

    public GameLoginHistoryPacket(uint lastLoginTimestamp, byte locationWarningFlag, string lastLoginLocation)
    {
        ArgumentNullException.ThrowIfNull(lastLoginLocation);

        if (lastLoginLocation.Contains('\0'))
        {
            throw new ArgumentException("Last-login location must not contain embedded null characters.", nameof(lastLoginLocation));
        }

        int locationLength = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetByteCount(lastLoginLocation);

        LastLoginTimestamp = lastLoginTimestamp;
        LocationWarningFlag = locationWarningFlag;
        LastLoginLocation = lastLoginLocation;
        PayloadLength = checked(sizeof(uint) + sizeof(byte) + locationLength + sizeof(byte));
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength { get; }
    public uint LastLoginTimestamp { get; }
    public byte LocationWarningFlag { get; }
    public string LastLoginLocation { get; }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(LastLoginTimestamp);
        writer.WriteByte(LocationWarningFlag);
        writer.WriteNullTerminatedString(LastLoginLocation);
    }
}
