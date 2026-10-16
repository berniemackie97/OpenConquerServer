using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameGhostTransitionRequestTests
{
    [Fact]
    public void Parse_AcceptsExactNativeAction145ButNotTheOlderServerAction137()
    {
        using GameInboundFrame valid = Frame(145);
        Assert.True(GameGhostTransitionRequest.TryParse(valid, out GameGhostTransitionRequest request));
        Assert.Equal(1_000_001u, request.EntityId);
        using GameInboundFrame older = Frame(137);
        Assert.False(GameGhostTransitionRequest.TryParse(older, out _));
    }

    [Theory]
    [InlineData(145, 0u, 0u)]
    [InlineData(145, 1_000_001u, 1u)]
    [InlineData(94, 1_000_001u, 0u)]
    public void Parse_RejectsForeignMalformedOrWrongVersionActions(ushort action, uint entityId, uint extra)
    {
        using GameInboundFrame frame = Frame(action, entityId, extra);
        Assert.False(GameGhostTransitionRequest.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(ushort action, uint entityId = 1_000_001, uint extra = 0)
    {
        byte[] wire = new byte[GameAction.FixedPacketLength];
        WireFrameHeader.Write(wire, (ushort)wire.Length, GameAction.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), extra);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(16), 0xAABBCCDDu);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(20), action);
        byte[] rented = ArrayPool<byte>.Shared.Rent(wire.Length);
        wire.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(wire, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
