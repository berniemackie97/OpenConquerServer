namespace OpenConquer.Protocol.Game.Packets;

public enum GameLoginProofParseError
{
    None = 0,
    InvalidLength,
    InvalidPacketId,
    InvalidMode,
    InvalidReservedField,
}
