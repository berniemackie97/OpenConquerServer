namespace OpenConquer.Protocol.Game.Packets;

public enum GameActionParseError
{
    None = 0,
    InvalidPacketId,
    TruncatedBody,
}
