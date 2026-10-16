using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameTradeQuitRequestTests
{
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)0xFFFF)]
    public void Parse_AcceptsExactNativeQuitLayout_WithoutInventingOpaqueTailSemantics(ushort tail)
    {
        using GameInboundFrame frame = Frame(1056, 12, 1_000_001, 2, tail);
        Assert.True(GameTradeQuitRequest.TryParse(frame, out GameTradeQuitRequest request));
        Assert.Equal(1_000_001u, request.EntityId);
    }

    [Theory]
    [InlineData((ushort)1056, 11, 1_000_001u, (ushort)2)]
    [InlineData((ushort)1056, 13, 1_000_001u, (ushort)2)]
    [InlineData((ushort)1056, 12, 0u, (ushort)2)]
    [InlineData((ushort)1056, 12, 1_000_001u, (ushort)1)]
    [InlineData((ushort)1056, 12, 1_000_001u, (ushort)3)]
    [InlineData((ushort)1040, 12, 1_000_001u, (ushort)2)]
    public void Parse_RejectsUnsupportedLengthIdentityTypeOrMode(ushort packetId, int length,
        uint entityId, ushort mode)
    {
        using GameInboundFrame frame = Frame(packetId, length, entityId, mode);
        Assert.False(GameTradeQuitRequest.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(ushort id, int length, uint entityId, ushort mode, ushort tail = 0)
    {
        byte[] bytes = new byte[length];
        WireFrameHeader.Write(bytes, checked((ushort)length), id);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), mode);
        if (length >= 12)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), tail);
        }
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        bytes.CopyTo(rented, 0);
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        return new GameInboundFrame(rented, header);
    }
}
