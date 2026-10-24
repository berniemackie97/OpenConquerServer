using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GamePortalRequestTests
{
    [Fact]
    public void PortalRequest_ReadsCandidateFromParameterPair_NotReportedHeroTile()
    {
        byte[] bytes = new byte[GameAction.FixedPacketLength];
        WireFrameHeader.Write(bytes, checked((ushort)bytes.Length), GameAction.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1000001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (380u << 16) | 430);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), GameAction.PortalRequestAction);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 150);

        using GameInboundFrame frame = Frame(bytes);
        Assert.True(GamePortalRequest.TryParse(frame, out GamePortalRequest request));
        Assert.Equal(1000001u, request.EntityId);
        Assert.Equal((ushort)430, request.CandidateX);
        Assert.Equal((ushort)380, request.CandidateY);
    }

    [Fact]
    public void PortalArrival_MatchesNativeClientReaderVector()
    {
        GameActionPacket packet = GameActionPacket.CreatePortalArrival(1000001, 1002, 430, 380, 0, 0);
        byte[] buffer = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        Assert.Equal(38, GameWireFrameEncoder.WriteFrame(packet, buffer));
        byte[] expected = Convert.FromHexString(
            "26001A2741420F00EA030000000000000000000056000000AE017C0100000000000000000000");
        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void PortalRequest_RejectsTrailingPayloadAndStrings()
    {
        byte[] bytes = new byte[GameAction.FixedPacketLength + 1];
        WireFrameHeader.Write(bytes, checked((ushort)bytes.Length), GameAction.PacketIdentifier);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), GameAction.PortalRequestAction);
        using GameInboundFrame frame = Frame(bytes);
        Assert.False(GamePortalRequest.TryParse(frame, out _));
    }

    private static GameInboundFrame Frame(byte[] bytes)
    {
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        byte[] rented = ArrayPool<byte>.Shared.Rent(bytes.Length);
        bytes.CopyTo(rented, 0);
        return new GameInboundFrame(rented, header);
    }
}
