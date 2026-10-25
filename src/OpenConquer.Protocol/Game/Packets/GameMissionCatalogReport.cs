using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// One frame of the mission catalog the 5517 client sends after login (1134, subtype 3). The client lists every MissionId
/// from its <c>ini/Questinfo.ini</c> in ascending order, at most 80 per frame, and writes nothing else into the 12-byte
/// records, so their state and value fields are zero. Only the ID range is kept because the server has no mission system;
/// the range is what shows that consecutive frames continue one catalog.
/// </summary>
public readonly record struct GameMissionCatalogReport(int MissionCount, int FirstMissionId, int LastMissionId)
{
    public const ushort PacketIdentifier = 1134;
    public const ushort CatalogSubtype = 3;
    public const int MaximumMissionsPerFrame = 80;

    private const int SubtypeOffset = 4;
    private const int MissionCountOffset = 6;
    private const int RecordsOffset = 8;
    private const int RecordLength = 12;
    private const int MissionIdLength = 4;

    public bool IsFull => MissionCount == MaximumMissionsPerFrame;

    public static int GetPacketLength(int missionCount) => RecordsOffset + (missionCount * RecordLength);

    public static bool TryParse(GameInboundFrame frame, out GameMissionCatalogReport report, out GameMissionCatalogReportParseError error)
    {
        ArgumentNullException.ThrowIfNull(frame);

        report = default;

        if (frame.PacketId != PacketIdentifier)
        {
            error = GameMissionCatalogReportParseError.InvalidPacketId;
            return false;
        }

        if (frame.Header.Length < RecordsOffset)
        {
            error = GameMissionCatalogReportParseError.InvalidPacketLength;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;

        if (BinaryPrimitives.ReadUInt16LittleEndian(packet[SubtypeOffset..]) != CatalogSubtype)
        {
            error = GameMissionCatalogReportParseError.UnsupportedSubtype;
            return false;
        }

        int missionCount = BinaryPrimitives.ReadUInt16LittleEndian(packet[MissionCountOffset..]);

        if (missionCount is 0 or > MaximumMissionsPerFrame)
        {
            error = GameMissionCatalogReportParseError.InvalidMissionCount;
            return false;
        }

        if (frame.Header.Length != GetPacketLength(missionCount))
        {
            error = GameMissionCatalogReportParseError.InvalidPacketLength;
            return false;
        }

        int previousMissionId = 0;

        for (int offset = RecordsOffset; offset < packet.Length; offset += RecordLength)
        {
            int missionId = BinaryPrimitives.ReadInt32LittleEndian(packet[offset..]);

            if (missionId <= 0)
            {
                error = GameMissionCatalogReportParseError.InvalidMissionId;
                return false;
            }

            if (packet.Slice(offset + MissionIdLength, RecordLength - MissionIdLength).ContainsAnyExcept((byte)0))
            {
                error = GameMissionCatalogReportParseError.NonZeroRecordField;
                return false;
            }

            if (missionId <= previousMissionId)
            {
                error = GameMissionCatalogReportParseError.MissionIdsNotAscending;
                return false;
            }

            previousMissionId = missionId;
        }

        report = new GameMissionCatalogReport(missionCount, BinaryPrimitives.ReadInt32LittleEndian(packet[RecordsOffset..]), previousMissionId);
        error = GameMissionCatalogReportParseError.None;
        return true;
    }
}
