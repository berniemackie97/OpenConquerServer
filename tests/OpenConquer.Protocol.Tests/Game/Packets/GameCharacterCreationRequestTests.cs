using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameCharacterCreationRequestTests
{
    [Fact]
    public void TryParse_DecodesVersionMatchedReferenceLayout()
    {
        byte[] packet = CreatePacket();
        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.False(request.CancelRequested);
        Assert.Equal("NewHero", request.Name);
        Assert.Equal((ushort)1003, request.Body);
        Assert.Equal((ushort)10, request.Profession);
        Assert.Equal("AABBCCDDEEFF", request.ClientReportedMacAddress);
    }

    [Fact]
    public void TryParse_CancelFlagDoesNotRequireCreationFields()
    {
        byte[] packet = CreatePacket();
        packet[4] = 1;
        packet.AsSpan(24, 15).Clear();
        packet.AsSpan(72, 4).Clear();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.True(request.CancelRequested);
        Assert.Equal(string.Empty, request.Name);
        Assert.Equal((ushort)0, request.Body);
        Assert.Equal((ushort)0, request.Profession);
    }

    [Fact]
    public void TryParse_TreatsNonzeroCancelFlagAsTrue()
    {
        byte[] packet = CreatePacket();
        packet[4] = 2;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.True(request.CancelRequested);
    }

    [Fact]
    public void TryParse_PreservesRawProfessionWithoutNormalizing()
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(74), ushort.MaxValue);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal(ushort.MaxValue, request.Profession);
    }

    [Fact]
    public void TryParse_HandlesNullTerminatedName()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 15).Fill(0);
        "Hero"u8.CopyTo(packet.AsSpan(24));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("Hero", request.Name);
    }

    [Fact]
    public void TryParse_AcceptsFullWidthNameWithoutNullTerminator()
    {
        byte[] packet = CreatePacket();
        "FifteenLettersX"u8.CopyTo(packet.AsSpan(24));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("FifteenLettersX", request.Name);
    }

    [Fact]
    public void TryParse_DecodesStrictWindows1252Name()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 15).Clear();
        packet[24] = (byte)'R';
        packet[25] = 0xE9;
        packet[26] = (byte)'n';
        packet[27] = (byte)'e';

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("Réne", request.Name);
    }

    [Fact]
    public void TryParse_RejectsUndefinedWindows1252Byte()
    {
        byte[] packet = CreatePacket();
        packet[24] = 0x81;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out _, out GameCharacterCreationParseError error));
        Assert.Equal(GameCharacterCreationParseError.InvalidTextEncoding, error);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketIdentifier()
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1002);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out _, out GameCharacterCreationParseError error));
        Assert.Equal(GameCharacterCreationParseError.InvalidPacketId, error);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(91)]
    [InlineData(1025)]
    public void TryParse_RejectsUnsupportedPacketLengths(int length)
    {
        byte[] packet = new byte[length];
        WireFrameHeader.Write(packet, checked((ushort)length), GameCharacterCreationRequest.PacketId);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out _, out GameCharacterCreationParseError error));
        Assert.Equal(GameCharacterCreationParseError.InvalidLength, error);
    }

    [Fact]
    public void TryParse_DoesNotInterpretUnverifiedTrailingFields()
    {
        byte[] packet = new byte[96];
        byte[] standard = CreatePacket();
        standard.CopyTo(packet, 0);
        WireFrameHeader.Write(packet, checked((ushort)packet.Length), GameCharacterCreationRequest.PacketId);
        packet.AsSpan(92, 4).Fill(0xA5);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("NewHero", request.Name);
        Assert.Equal((ushort)10, request.Profession);
    }

    [Fact]
    public void TryParse_RejectsNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GameCharacterCreationRequest.TryParse(null!, out _, out _));
    }

    private static byte[] CreatePacket()
    {
        byte[] packet = new byte[GameCharacterCreationRequest.MinimumPacketLength];

        WireFrameHeader.Write(packet, checked((ushort)packet.Length), GameCharacterCreationRequest.PacketId);

        "NewHero"u8.CopyTo(packet.AsSpan(24, 15));

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(72), 1003);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(74), 10);

        "AABBCCDDEEFF"u8.CopyTo(packet.AsSpan(80, 12));

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
