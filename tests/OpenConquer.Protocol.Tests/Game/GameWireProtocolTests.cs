using OpenConquer.Protocol.Game;

namespace OpenConquer.Protocol.Tests.Game;

public sealed class GameWireProtocolTests
{
    [Fact]
    public void Constants_Match5517GameTransport()
    {
        Assert.Equal(0x400, GameWireProtocol.MaximumPacketLength);
        Assert.Equal(8, GameWireProtocol.SignatureLength);
        Assert.Equal(0x408, GameWireProtocol.MaximumWireFrameLength);
        Assert.Equal("TQClient"u8.ToArray(), GameWireProtocol.ClientSignature.ToArray());
        Assert.Equal("TQServer"u8.ToArray(), GameWireProtocol.ServerSignature.ToArray());
    }
}
