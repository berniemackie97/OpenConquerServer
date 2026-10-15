using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameWalkPacketTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(0x0100_0007u)]
    public void TryParse_AcceptsNativeEightDirectionPacketWithoutDiscardingOpaqueFields(uint packedDirection)
    {
        byte[] wire = CreatePacket(packedDirection: packedDirection, selector: 0, entityId: 500, tick: 0xABCD_EF01, mapState: 0x1234_5678);

        Assert.True(GameWalkPacket.TryParse(wire, out GameWalkPacket walk));
        Assert.Equal(packedDirection, walk.PackedDirection);
        Assert.Equal((byte)packedDirection, walk.DirectionIndex);
        Assert.Equal(500u, walk.EntityId);
        Assert.Equal(0u, walk.CommandSelector);
        Assert.Equal(0xABCD_EF01u, walk.MovementTick);
        Assert.Equal(0x1234_5678u, walk.MapState);
        Assert.Equal(GameWalkPacket.PacketIdentifier, walk.PacketId);
        Assert.Equal(20, walk.PayloadLength);
    }

    [Fact]
    public void TryParse_NonNineSelectorUsesNativeEightDirectionBranch()
    {
        Assert.True(GameWalkPacket.TryParse(CreatePacket(selector: 8), out _));
        Assert.True(GameWalkPacket.TryParse(CreatePacket(selector: 10), out _));
        Assert.True(GameWalkPacket.TryParse(CreatePacket(selector: 9), out GameWalkPacket twoTile));
        Assert.True(twoTile.IsTwentyFourDirection);
        Assert.Equal((byte)7, twoTile.DirectionIndex);
    }

    [Fact]
    public void WritePayload_EchoesOriginalNativeFieldsByteForByte()
    {
        byte[] original = CreatePacket(packedDirection: 0xA1B2_C307, entityId: 500,
            selector: 10, tick: 0xFFFF_FFF0, mapState: 0x1234_5678);
        Assert.True(GameWalkPacket.TryParse(original, out GameWalkPacket walk));

        byte[] payload = new byte[walk.PayloadLength];
        PacketWriter writer = new(payload);
        walk.WritePayload(ref writer);

        Assert.Equal(walk.PayloadLength, writer.Written);
        Assert.True(original.AsSpan(4).SequenceEqual(writer.WrittenSpan));
    }

    [Theory]
    [InlineData(8u)]
    [InlineData(255u)]
    [InlineData(0x0100_0008u)]
    public void TryParse_NormalizesTheNativeLowDirectionByte(uint direction)
    {
        Assert.True(GameWalkPacket.TryParse(CreatePacket(packedDirection: direction), out GameWalkPacket walk));
        Assert.Equal((byte)(unchecked((byte)direction) % 8), walk.DirectionIndex);
        Assert.Equal(direction, walk.PackedDirection);
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(23u, 23)]
    [InlineData(24u, 0)]
    [InlineData(47u, 23)]
    [InlineData(0xA1B2_C3FFu, 15)]
    public void TryParse_NormalizesOnlyLowByteForExactSelectorNine(uint packed, byte expected)
    {
        Assert.True(GameWalkPacket.TryParse(CreatePacket(packedDirection: packed, selector: 9), out GameWalkPacket walk));
        Assert.True(walk.IsTwentyFourDirection);
        Assert.Equal(expected, walk.DirectionIndex);
        Assert.Equal((byte)(expected % 8), walk.FacingDirection);
        Assert.Equal(packed, walk.PackedDirection);
    }

    [Fact]
    public void TryParse_RejectsWrongEntityAndHeader()
    {
        Assert.False(GameWalkPacket.TryParse(CreatePacket(entityId: 0), out _));

        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet, 25);
        Assert.False(GameWalkPacket.TryParse(packet, out _));

        packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1052);
        Assert.False(GameWalkPacket.TryParse(packet, out _));
        Assert.False(GameWalkPacket.TryParse(CreatePacket().AsSpan(0, 23), out _));
    }

    private static byte[] CreatePacket(uint packedDirection = 7, uint entityId = 42, uint selector = 0, uint tick = 100, uint mapState = 0)
    {
        byte[] packet = new byte[GameWalkPacket.PacketLength];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, GameWalkPacket.PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), GameWalkPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), packedDirection);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), selector);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), tick);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20), mapState);
        return packet;
    }
}
