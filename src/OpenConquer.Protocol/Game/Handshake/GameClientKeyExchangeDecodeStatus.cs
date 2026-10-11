namespace OpenConquer.Protocol.Game.Handshake;

public enum GameClientKeyExchangeDecodeStatus
{
    Success = 0,
    NeedMoreData,
    InvalidDeclaredLength,
    InvalidSignature,
    InvalidPayload,
}
