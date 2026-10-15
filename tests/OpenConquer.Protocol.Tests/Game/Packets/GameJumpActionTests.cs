using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameJumpActionTests
{
    [Fact]
    public void Parse_UsesAbsoluteUnsignedTargetAndPreservesFullEcho()
    {
        using GameInboundFrame frame = Frame(GameJumpAction.JumpAction);
        Assert.True(GameJumpAction.TryParse(frame, out GameJumpAction jump));
        Assert.Equal(0x11223344u, jump.EntityId);
        Assert.Equal((ushort)65000, jump.TargetX);
        Assert.Equal((ushort)41000, jump.TargetY);
        Assert.Equal(567u, jump.Timestamp);

        GameActionPacket echo = jump.CreateAcknowledgement();
        byte[] bytes = new byte[GameWireFrameEncoder.GetFrameLength(echo)];
        GameWireFrameEncoder.WriteFrame(echo, bytes);
        Assert.Equal(frame.Packet.ToArray(), bytes);
    }

    [Theory]
    [InlineData((ushort)79, 0x11223344u, (byte)0, 38, (ushort)10010)]
    [InlineData((ushort)137, 0u, (byte)0, 38, (ushort)10010)]
    [InlineData((ushort)137, 0x11223344u, (byte)1, 38, (ushort)10010)]
    [InlineData((ushort)137, 0x11223344u, (byte)0, 39, (ushort)10010)]
    [InlineData((ushort)137, 0x11223344u, (byte)0, 38, (ushort)10005)]
    public void Parse_RejectsWrongActionIdentityStringsLengthOrPacket(ushort action, uint entity,
        byte strings, int length, ushort packetId)
    {
        using GameInboundFrame frame = Frame(action, entity, strings, length, packetId);
        Assert.False(GameJumpAction.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(ushort action, uint entity = 0x11223344u,
        byte strings = 0, int length = GameAction.FixedPacketLength,
        ushort packetId = GameAction.PacketIdentifier)
    {
        byte[] wire = new byte[length];
        WireFrameHeader.Write(wire, checked((ushort)length), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), entity);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), (41000u << 16) | 65000);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(12), 0x12345678u);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(16), 567);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(20), action);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(22), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(24), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(26), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(28), 0xCAFEBABEu);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(32), 0x11223344u);
        wire[36] = 7;
        wire[37] = strings;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        wire.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(wire, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
