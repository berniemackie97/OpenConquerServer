using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Tests.Game;

public sealed class GameWireFrameEncoderTests
{
    [Fact]
    public void GetFrameLength_AllowsPacketExactlyAtNativeMaximum()
    {
        IPacket packet = new DeclaredLengthPacket(packetId: 0x1234, payloadLength: GameWireProtocol.MaximumPacketLength - WireFrameHeader.Size);

        int length = GameWireFrameEncoder.GetFrameLength(packet);

        Assert.Equal(GameWireProtocol.MaximumPacketLength, length);
    }

    [Fact]
    public void GetFrameLength_RejectsPacketAboveNativeMaximum()
    {
        IPacket packet = new DeclaredLengthPacket(packetId: 0x1234, payloadLength: GameWireProtocol.MaximumPacketLength - WireFrameHeader.Size + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.GetFrameLength(packet));

        Assert.Equal("Packet 4660 declares a 1025-byte frame, which exceeds the 1024-byte maximum.", exception.Message);
    }

    [Fact]
    public void WriteFrame_WritesPacketExactlyAtNativeMaximum()
    {
        byte[] payload = new byte[GameWireProtocol.MaximumPacketLength - WireFrameHeader.Size];
        IPacket packet = new TestPacket(packetId: 0x1234, payload);
        byte[] destination = new byte[GameWireProtocol.MaximumPacketLength];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameWireProtocol.MaximumPacketLength, written);
        Assert.True(WireFrameHeader.TryRead(destination, out WireFrameHeader header));
        Assert.Equal(GameWireProtocol.MaximumPacketLength, header.Length);
        Assert.Equal(0x1234, header.PacketId);
    }

    [Fact]
    public void WriteFrame_RejectsPacketAboveNativeMaximumWithoutModifyingDestination()
    {
        IPacket packet = new DeclaredLengthPacket(packetId: 0x1234, payloadLength: GameWireProtocol.MaximumPacketLength - WireFrameHeader.Size + 1);
        byte[] destination = Enumerable.Repeat((byte)0xCC, GameWireProtocol.MaximumPacketLength + 1).ToArray();

        Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.All(destination, value => Assert.Equal(0xCC, value));
    }

    [Fact]
    public void GenericWireEncoder_RemainsIndependentOfGamePacketLimit()
    {
        IPacket packet = new DeclaredLengthPacket(packetId: 0x1234, payloadLength: GameWireProtocol.MaximumPacketLength);

        int length = WireFrameEncoder.GetFrameLength(packet);

        Assert.Equal(GameWireProtocol.MaximumPacketLength + WireFrameHeader.Size, length);
    }

    private sealed class TestPacket(ushort packetId, byte[] payload) : IPacket
    {
        public ushort PacketId => packetId;
        public int PayloadLength => payload.Length;

        public void WritePayload(ref PacketWriter writer) => writer.WriteBytes(payload);
    }

    private sealed class DeclaredLengthPacket(ushort packetId, int payloadLength) : IPacket
    {
        public ushort PacketId => packetId;
        public int PayloadLength => payloadLength;

        public void WritePayload(ref PacketWriter writer) => writer.Reserve(payloadLength);
    }
}
