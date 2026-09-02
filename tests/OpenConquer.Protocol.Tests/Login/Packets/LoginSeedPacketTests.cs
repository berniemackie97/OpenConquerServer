using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginSeedPacketTests
{
    [Fact]
    public void Packet_ExposesVerifiedIdentifierAndPayloadLength()
    {
        LoginSeedPacket packet = new(seed: 0x1234_5678);

        Assert.Equal((ushort)1059, packet.PacketId);

        Assert.Equal(sizeof(uint), packet.PayloadLength);

        Assert.Equal(0x1234_5678u, packet.Seed);
    }

    [Fact]
    public void WireFrameEncoder_WritesVerifiedSeedFrame()
    {
        LoginSeedPacket packet = new(seed: 0x1234_5678);

        byte[] destination = new byte[WireFrameHeader.Size + sizeof(uint)];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected = [0x08, 0x00, 0x23, 0x04, 0x78, 0x56, 0x34, 0x12];

        Assert.Equal(expected.Length, written);

        Assert.Equal(expected, destination);
    }
}
