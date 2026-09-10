using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameLoginProof1052Tests
{
    [Fact]
    public void TryParse_ParsesExactNative5517Layout()
    {
        byte[] packet = BuildValidPacket();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameLoginProof1052.TryParse(frame, out GameLoginProof1052 proof, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.None, error);
        Assert.Equal(0x10203040U, proof.SessionUid);
        Assert.Equal(0x50607080U, proof.AuthenticationKey);
        Assert.Equal(GameLoginProof1052.ExpectedMode, proof.Mode);
        Assert.Equal((ushort)0x6E45, proof.LocaleTag);
        Assert.Equal(0x0000_6655_4433_2211UL, proof.HardwareAddress);
        Assert.Equal(5517, proof.ResourceVersion);
    }

    [Fact]
    public void TryParse_AllowsNativeZeroHardwareAddressFallback()
    {
        byte[] packet = BuildValidPacket();
        packet.AsSpan(16, 6).Clear();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameLoginProof1052.TryParse(frame, out GameLoginProof1052 proof, out _));
        Assert.Equal(0UL, proof.HardwareAddress);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketLength()
    {
        byte[] packet = new byte[27];
        WireFrameHeader.Write(packet, 27, GameLoginProof1052.PacketId);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameLoginProof1052.TryParse(frame, out _, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.InvalidLength, error);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketId()
    {
        byte[] packet = BuildValidPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1053);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameLoginProof1052.TryParse(frame, out _, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.InvalidPacketId, error);
    }

    [Fact]
    public void TryParse_RejectsWrongMode()
    {
        byte[] packet = BuildValidPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), 0x7D);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameLoginProof1052.TryParse(frame, out _, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.InvalidMode, error);
    }

    [Fact]
    public void TryParse_RejectsNonzeroNativeReservedField()
    {
        byte[] packet = BuildValidPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22), 1);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameLoginProof1052.TryParse(frame, out _, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.InvalidReservedField, error);
    }

    [Fact]
    public void TryParse_ThrowsForNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => GameLoginProof1052.TryParse(null!, out _, out _));
    }

    private static byte[] BuildValidPacket()
    {
        byte[] packet = new byte[GameLoginProof1052.PacketLength];

        WireFrameHeader.Write(packet, GameLoginProof1052.PacketLength, GameLoginProof1052.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 0x10203040);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0x50607080);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), GameLoginProof1052.ExpectedMode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), 0x6E45);

        packet[16] = 0x11;
        packet[17] = 0x22;
        packet[18] = 0x33;
        packet[19] = 0x44;
        packet[20] = 0x55;
        packet[21] = 0x66;

        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24), 5517);
        return packet;
    }

    private static GameInboundFrame CreateFrame(byte[] packet)
    {
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(buffer, 0);

        return new GameInboundFrame(buffer, header);
    }
}
