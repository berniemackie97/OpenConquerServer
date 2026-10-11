namespace OpenConquer.Protocol.Game.Framing;

public readonly record struct GameSecuredFrameDecodeResult(GameSecuredFrameDecodeStatus Status, int BytesConsumed, GameInboundFrame? Frame)
{
    public bool Succeeded => Status == GameSecuredFrameDecodeStatus.Success;
}
