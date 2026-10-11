namespace OpenConquer.Protocol.Game.Framing;

public enum GameSecuredFrameDecodeStatus
{
    Success = 0,
    NeedMoreData,
    InvalidPacketLength,
    InvalidSignature,
}
