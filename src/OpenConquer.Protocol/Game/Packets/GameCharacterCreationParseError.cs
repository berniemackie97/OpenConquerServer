namespace OpenConquer.Protocol.Game.Packets;

public enum GameCharacterCreationParseError
{
    None = 0,
    InvalidPacketId,
    InvalidLength,
    InvalidTextEncoding,
}
