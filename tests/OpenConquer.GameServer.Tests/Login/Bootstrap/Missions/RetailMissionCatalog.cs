using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Missions;

/// <summary>
/// The mission catalog (1134, subtype 3) the retail 5517 client sends after ANSWER_OK. It lists the 224 distinct MissionIds
/// of the shipped <c>ini/Questinfo.ini</c> (SHA-256 98d92e41bb092f406e601a6e728f1a4758561b82523fd8d7001daaa112185bad,
/// TotalMission=230) in ascending order, 80 per frame, with every other record byte zero. The retail run on 2026-10-25
/// recorded the first frame as 968 bytes beginning C8036E04 0300 5000 58020000.
/// </summary>
internal static class RetailMissionCatalog
{
    private const int RecordsOffset = 8;
    private const int RecordLength = 12;

    private static readonly (int First, int Last)[] s_missionIdRanges =
    [
        (600, 608), (610, 612), (650, 660), (662, 665), (669, 669), (701, 704), (706, 707), (709, 710), (800, 800), (802, 802),
        (804, 804), (807, 836), (920, 921), (925, 930), (933, 934), (938, 938), (950, 967), (986, 989), (991, 993), (999, 1002),
        (1006, 1021), (1023, 1023), (1027, 1030), (1100, 1105), (1107, 1108), (1110, 1112), (1114, 1118), (1120, 1121), (1123, 1123),
        (1125, 1131), (1133, 1135), (1141, 1146), (1200, 1201), (1400, 1400), (1560, 1562), (1577, 1589), (1701, 1701), (1761, 1763),
        (1928, 1930), (1956, 1956), (1962, 1962), (1968, 1970), (1972, 1972), (2102, 2102), (6001, 6001), (6020, 6025), (6047, 6047),
        (6049, 6051), (6069, 6080), (6126, 6126), (6184, 6184), (20001, 20001),
    ];

    public static int[] MissionIds { get; } = s_missionIdRanges.SelectMany(static range => Enumerable.Range(range.First, range.Last - range.First + 1)).ToArray();

    /// <summary>
    /// The three frames in the order the client sends them: 80, 80 and 64 records.
    /// </summary>
    public static byte[][] Frames { get; } = MissionIds.Chunk(GameMissionCatalogReport.MaximumMissionsPerFrame).Select(Build).ToArray();

    /// <summary>
    /// Builds one frame the way the client's builder does: zeroed buffer, then type, subtype, count and one MissionId per record.
    /// </summary>
    public static byte[] Build(int[] missionIds)
    {
        int length = GameMissionCatalogReport.GetPacketLength(missionIds.Length);
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameMissionCatalogReport.PacketIdentifier);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), GameMissionCatalogReport.CatalogSubtype);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), checked((ushort)missionIds.Length));

        for (int index = 0; index < missionIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(RecordsOffset + (index * RecordLength)), missionIds[index]);
        }

        return packet;
    }
}
