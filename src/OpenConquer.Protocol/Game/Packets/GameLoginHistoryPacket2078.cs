using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client account-login history and location-alert packet.
/// </summary>
public sealed class GameLoginHistoryPacket2078 : IPacket
{
    public const ushort PacketIdentifier = 2078;

    public GameLoginHistoryPacket2078(uint lastLoginTimestamp, byte locationWarningFlag, string lastLoginLocation)
    {
        ArgumentNullException.ThrowIfNull(lastLoginLocation);

        if (lastLoginLocation.Contains('\0'))
        {
            throw new ArgumentException("Last-login location must not contain embedded null characters.", nameof(lastLoginLocation));
        }

        int locationLength = TqEncoding.Resolve(TqTextEncoding.Ansi).GetByteCount(lastLoginLocation);

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
