namespace OpenConquer.Protocol.Game.Handshake;

public readonly record struct GameClientKeyExchangeDecodeResult(GameClientKeyExchangeDecodeStatus Status, int BytesConsumed, int RequiredBytes, string? ClientPublicKeyHex)
{
    public bool Succeeded => Status == GameClientKeyExchangeDecodeStatus.Success;
}
