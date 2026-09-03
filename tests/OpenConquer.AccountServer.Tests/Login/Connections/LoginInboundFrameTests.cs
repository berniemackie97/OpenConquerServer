using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginInboundFrameTests
{
    [Fact]
    public void Dispose_ClearsPlaintextStorageAndIsIdempotent()
    {
        byte[] storage = Convert.FromHexString("0800230478563412");

        LoginInboundFrame frame = new(storage, frameLength: 8, LoginSeedPacket.PacketIdentifier);

        ReadOnlyMemory<byte> payload = frame.Payload;

        Assert.Equal(Convert.FromHexString("78563412"), payload.ToArray());

        frame.Dispose();

        Assert.All(storage, value => Assert.Equal(0, value));

        Assert.All(payload.ToArray(), value => Assert.Equal(0, value));

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = frame.Payload;
        });

        frame.Dispose();
    }
}
