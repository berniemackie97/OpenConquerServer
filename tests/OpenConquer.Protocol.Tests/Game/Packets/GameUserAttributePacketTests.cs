using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameUserAttributePacketTests
{
    [Fact]
    public void Encode_PrimaryStatusHasFull128BitValueAtNativeOffsets()
    {
        GameUserAttributePacket packet = new(1_000_001, GameUserAttributePacket.PrimaryStatusAttribute,
            1UL << 5 | 1UL << 10, 1UL << (111 - 64));
        byte[] wire = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, wire);

        Assert.Equal(32, wire.Length);
        Assert.Equal((ushort)10017, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(2)));
        Assert.Equal(1_000_001u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8)));
        Assert.Equal(25u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(12)));
        Assert.Equal(1UL << 5 | 1UL << 10, BinaryPrimitives.ReadUInt64LittleEndian(wire.AsSpan(16)));
        Assert.Equal(1UL << (111 - 64), BinaryPrimitives.ReadUInt64LittleEndian(wire.AsSpan(24)));
    }
}
