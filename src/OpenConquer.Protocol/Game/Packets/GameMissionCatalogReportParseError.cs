namespace OpenConquer.Protocol.Game.Packets;

public enum GameMissionCatalogReportParseError
{
    None = 0,
    InvalidPacketId,
    InvalidPacketLength,
    UnsupportedSubtype,
    InvalidMissionCount,
    InvalidMissionId,
    NonZeroRecordField,
    MissionIdsNotAscending,
}
