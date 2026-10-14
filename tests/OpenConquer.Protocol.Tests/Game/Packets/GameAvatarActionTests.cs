using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameAvatarActionTests
{
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)7)]
    public void Turn_ParsesNativeDirectionAtOffsetTwentyTwo(ushort direction)
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction,
            direction: direction, parameter: 0xFFFF_FFFF);
        Assert.True(GameAvatarAction.TryParse(frame, out GameAvatarAction action));
        Assert.Equal((byte)direction, action.Direction);
        Assert.Equal((ushort)0, action.Pose);
        Assert.Equal(123u, action.Timestamp);

        GameActionPacket acknowledgement = action.CreateAcknowledgement();
        byte[] wire = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
        GameWireFrameEncoder.WriteFrame(acknowledgement, wire);
        Assert.Equal((ushort)GameAvatarAction.DirectionAction,
            BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(20)));
        Assert.Equal(direction, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(22)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8)));
        Assert.Equal(123u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(16)));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(8u)]
    [InlineData(100u)]
    [InlineData(150u)]
    [InlineData(160u)]
    [InlineData(170u)]
    [InlineData(190u)]
    [InlineData(200u)]
    [InlineData(210u)]
    [InlineData(230u)]
    [InlineData(250u)]
    [InlineData(270u)]
    public void Pose_UsesNativeDirectionAndNormalizesUntrustedSecondaryRoleId(uint pose)
    {
        // In native action 81 the DWORD at +16 can also be resolved as a role ID.
        using GameInboundFrame frame = Frame(GameAvatarAction.PoseAction,
            parameter: pose, direction: 7, timestamp: 1_000_200);
        Assert.True(GameAvatarAction.TryParse(frame, out GameAvatarAction action));
        Assert.Equal((ushort)pose, action.Pose);
        Assert.Equal((byte)7, action.Direction);
        Assert.Equal(1_000_200u, action.Timestamp);

        GameActionPacket acknowledgement = action.CreateAcknowledgement();
        Assert.Equal(pose, acknowledgement.ParameterPair);
        Assert.Equal((ushort)7, acknowledgement.Direction);
        Assert.Equal(0u, acknowledgement.Timestamp);
        Assert.Equal(0u, acknowledgement.ActionParameter);
        Assert.Equal(0u, acknowledgement.Data1);
        Assert.Equal(0u, acknowledgement.Data2);
        Assert.Equal((ushort)0, acknowledgement.PositionX);
        Assert.Equal((ushort)0, acknowledgement.PositionY);

        byte[] wire = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
        GameWireFrameEncoder.WriteFrame(acknowledgement, wire);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(16)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(22)));
    }

    [Theory]
    [InlineData((ushort)79, 0u, (ushort)8)]
    [InlineData((ushort)81, 100u, (ushort)8)]
    [InlineData((ushort)81, 100u, (ushort)333)]
    [InlineData((ushort)81, 0u, (ushort)0)]
    [InlineData((ushort)81, 9u, (ushort)0)]
    [InlineData((ushort)81, 65536u, (ushort)0)]
    [InlineData((ushort)137, 100u, (ushort)0)]
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
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction,
            identity: identity, strings: strings, length: length);
        Assert.False(GameAvatarAction.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsWrongPacketId()
    {
        using GameInboundFrame frame = Frame(GameAvatarAction.DirectionAction,
            packetId: GameWalkPacket.PacketIdentifier);
        Assert.False(GameAvatarAction.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(ushort kind, uint identity = 55, uint parameter = 0,
        ushort direction = 2, byte strings = 0, int length = GameAction.FixedPacketLength,
        ushort packetId = GameAction.PacketIdentifier, uint timestamp = 123)
    {
        byte[] bytes = new byte[length];
        WireFrameHeader.Write(bytes, checked((ushort)length), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), identity);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), kind);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), direction);
        bytes[37] = strings;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        bytes.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
