namespace OpenConquer.Protocol.Game.Packets;

public enum GameSocialRelationParseError
{
    None = 0,
    InvalidPacketId,
    InvalidPacketLength,
    MissingNameTerminator,
}
