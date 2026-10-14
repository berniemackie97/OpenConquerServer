using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GamePlayerSpawnPacketTests
{
    [Fact]
    public void Encode_MatchesNative5517PlayerLayoutIncludingEquipmentAndFourStrings()
    {
        GamePlayerSpawnPacket packet = new(new GamePlayerSpawnSnapshot(
            1_000_100, "TestPlayer", 2003, 123, 500, 750, 6, 25, 101, 2, 15, 11, 140, 7, 99,
            111_111, 222_222, 333_333, 444_444));
        byte[] wire = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        Assert.Equal(wire.Length, GameWireFrameEncoder.WriteFrame(packet, wire));

        Assert.Equal((ushort)wire.Length, BinaryPrimitives.ReadUInt16LittleEndian(wire));
        Assert.Equal((ushort)10014, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(2)));
        Assert.Equal(2003u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4)));
        Assert.Equal(1_000_100u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8)));
        Assert.Equal(99u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(16)));
        Assert.Equal(111_111u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0x28)));
        Assert.Equal(222_222u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0x30)));
        Assert.Equal(333_333u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0x34)));
        Assert.Equal(444_444u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0x38)));
        Assert.Equal(25u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0x50)));
        Assert.Equal((ushort)123, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0x54)));
        Assert.Equal((ushort)500, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0x56)));
        Assert.Equal((ushort)750, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0x58)));
        Assert.Equal((byte)6, wire[0x5A]);
        Assert.Equal((byte)2, wire[0x62]);
        Assert.Equal((ushort)101, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0x63)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0xA7)));
        Assert.Equal(111_111u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0xB6)));
        Assert.Equal(222_222u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0xBA)));
        Assert.Equal(333_333u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0xBE)));
        Assert.Equal(444_444u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0xC2)));
        Assert.Equal((ushort)15, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0xCF)));
        Assert.Equal((ushort)11, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0xD1)));
        Assert.Equal((ushort)140, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0xD3)));
        Assert.Equal((byte)0, wire[0xD9]);
        Assert.Equal((byte)4, wire[0xDA]);
        Assert.Equal((byte)10, wire[0xDB]);
        Assert.Equal("TestPlayer", System.Text.Encoding.ASCII.GetString(wire.AsSpan(0xDC, 10)));
        Assert.Equal(0, wire[^1]);
        Assert.Equal(4 + 214 + 1 + 4 + 10, wire.Length);
    }

    [Theory]
    [InlineData(0u, "Valid")]
    [InlineData(999_999u, "Valid")]
    [InlineData(1_000_000u, "")]
    [InlineData(1_000_000u, "1234567890123456")]
    public void Encode_RejectsUnrepresentablePlayerIdentityOrName(uint id, string name)
    {
        GamePlayerSpawnSnapshot snapshot = Create(id, name);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GamePlayerSpawnPacket(snapshot));
    }

    [Fact]
    public void Encode_RejectsInvalidDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GamePlayerSpawnPacket(Create(1_000_000, "Valid") with { Direction = 8 }));
    }

    private static GamePlayerSpawnSnapshot Create(uint id, string name) =>
        new(id, name, 1000, 0, 100, 100, 0, 1, 1, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0);
}
