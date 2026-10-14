using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameAvatarActionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Turn_ParsesNativeDirectionAtOffsetTwentyTwo(ushort direction)
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction, direction: direction, parameter: 0xFFFF_FFFF);
        Assert.True(GameAvatarAction.TryParse(frame, out GameAvatarAction action));
        Assert.Equal((byte)direction, action.Direction);
        Assert.Equal((ushort)0, action.Pose);
        Assert.Equal(123u, action.Timestamp);
        GameActionPacket acknowledgement = action.CreateAcknowledgement();
        byte[] wire = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
        GameWireFrameEncoder.WriteFrame(acknowledgement, wire);
        Assert.Equal((ushort)GameAvatarAction.DirectionAction, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(20)));
        Assert.Equal(direction, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(22)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8)));
    }

    [Theory]
    [InlineData((uint)1)]
    [InlineData((uint)8)]
    [InlineData((uint)100)]
    [InlineData((uint)150)]
    [InlineData((uint)160)]
    [InlineData((uint)170)]
    [InlineData((uint)190)]
    [InlineData((uint)200)]
    [InlineData((uint)210)]
    [InlineData((uint)230)]
    [InlineData((uint)250)]
    [InlineData((uint)270)]
    public void Pose_UsesNativeParameterPairAndNormalizesUntrustedFields(uint pose)
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.PoseAction, parameter: pose, direction: 333);
        Assert.True(GameAvatarAction.TryParse(frame, out GameAvatarAction action));
        Assert.Equal((ushort)pose, action.Pose);
        Assert.Equal((byte)0, action.Direction);
        GameActionPacket acknowledgement = action.CreateAcknowledgement();
        Assert.Equal(pose, acknowledgement.ParameterPair);
        Assert.Equal((ushort)0, acknowledgement.Direction);
        Assert.Equal(0u, acknowledgement.ActionParameter);
        Assert.Equal(0u, acknowledgement.Data1);
        Assert.Equal(0u, acknowledgement.Data2);
    }

    [Theory]
    [InlineData((ushort)79, (uint)0, (ushort)8)]
    [InlineData((ushort)81, (uint)0, (ushort)0)]
    [InlineData((ushort)81, (uint)9, (ushort)0)]
    [InlineData((ushort)81, (uint)65536, (ushort)0)]
    [InlineData((ushort)137, (uint)100, (ushort)0)]
    public void RejectsUnsupportedDirectionPoseAndAction(ushort kind, uint pose, ushort direction)
    {
        using GameInboundFrame frame = Frame(kind, parameter: pose, direction: direction);
        Assert.False(GameAvatarAction.TryParse(frame, out _));
    }

    [Theory]
    [InlineData(0u, (byte)0, 38)]
    [InlineData(55u, (byte)1, 38)]
    [InlineData(55u, (byte)0, 39)]
    public void RejectsMalformedIdentityStringListAndLength(uint identity, byte strings, int length)
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction, identity: identity, strings: strings, length: length);
        Assert.False(GameAvatarAction.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsWrongPacketId()
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction, packetId: GameWalkPacket.PacketIdentifier);
        Assert.False(GameAvatarAction.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(ushort kind, uint identity = 55, uint parameter = 0,
        ushort direction = 2, byte strings = 0, int length = GameAction.FixedPacketLength,
        ushort packetId = GameAction.PacketIdentifier)
    {
        byte[] bytes = new byte[length];
        WireFrameHeader.Write(bytes, checked((ushort)length), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), identity);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 123);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), kind);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), direction);
        bytes[37] = strings;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        bytes.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
