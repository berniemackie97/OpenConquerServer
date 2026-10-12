using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameCharacterCreationRequestTests
{
    [Fact]
    public void TryParse_DecodesNative5517Layout()
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
        Assert.Equal(0x12345678u, request.ClientReportedSessionUid);
        Assert.Equal("AABBCCDDEEFF", request.ClientReportedMacAddress);
    }

    [Fact]
    public void TryParse_DecodesNativeCancellationRequest()
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 1);
        packet.AsSpan(24, 48).Clear();
        packet.AsSpan(72, 4).Clear();
        packet.AsSpan(80, 40).Clear();

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.True(request.CancelRequested);
        Assert.Equal(string.Empty, request.Name);
        Assert.Equal((ushort)0, request.Body);
        Assert.Equal((ushort)0, request.Profession);
        Assert.Equal(0x12345678u, request.ClientReportedSessionUid);
        Assert.Equal(string.Empty, request.ClientReportedMacAddress);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(256u)]
    [InlineData(0x01000000u)]
    [InlineData(uint.MaxValue)]
    public void TryParse_RejectsInvalidCancellationFlag(uint value)
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), value);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidCancelFlag, error);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(23)]
    [InlineData(120)]
    public void TryParse_RejectsNonzeroVerifiedReservedBytes(int offset)
    {
        byte[] packet = CreatePacket();
        packet[offset] = 0xA5;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidReservedField, error);
    }

    [Fact]
    public void TryParse_DoesNotInterpretUnverifiedTrailingBytes()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(121, 3).Fill(0xA5);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.Equal("NewHero", request.Name);
        Assert.Equal(0x12345678u, request.ClientReportedSessionUid);
    }

    [Fact]
    public void TryParse_PreservesRawBodyWithoutNormalizing()
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(72), ushort.MaxValue);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal(ushort.MaxValue, request.Body);
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
        packet.AsSpan(24, 48).Clear();
        "Hero"u8.CopyTo(packet.AsSpan(24));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("Hero", request.Name);
    }

    [Fact]
    public void TryParse_AcceptsMaximumNativeClientNameLength()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 48).Clear();
        "FifteenLettersX"u8.CopyTo(packet.AsSpan(24));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("FifteenLettersX", request.Name);
    }

    [Fact]
    public void TryParse_RejectsNameWithoutTerminatorWithinNativeInputCapacity()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 16).Fill((byte)'A');
        packet[40] = 0;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidNameField, error);
    }

    [Fact]
    public void TryParse_DecodesStrictWindows1252Name()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 48).Clear();
        packet[24] = (byte)'R';
        packet[25] = 0xE9;
        packet[26] = (byte)'n';
        packet[27] = (byte)'e';

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request, out _));
        Assert.Equal("Réne", request.Name);
    }

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x8D)]
    [InlineData(0x8F)]
    [InlineData(0x90)]
    [InlineData(0x9D)]
    public void TryParse_RejectsUndefinedWindows1252NameByte(int value)
    {
        byte[] packet = CreatePacket();
        packet[24] = checked((byte)value);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidTextEncoding, error);
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x81)]
    [InlineData(0xE9)]
    [InlineData(0xFF)]
    public void TryParse_RejectsNonAsciiMacAddressByte(int value)
    {
        byte[] packet = CreatePacket();
        packet[80] = checked((byte)value);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidTextEncoding, error);
    }

    [Fact]
    public void TryParse_IgnoresBytesAfterTextTerminators()
    {
        byte[] packet = CreatePacket();

        packet.AsSpan(24, 48).Clear();
        packet[24] = (byte)'A';
        packet[25] = 0;
        packet[26] = 0x81;
        packet[40] = 0xA5;

        packet.AsSpan(80, 40).Clear();
        packet[80] = (byte)'B';
        packet[81] = 0;
        packet[82] = 0xFF;

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.Equal("A", request.Name);
        Assert.Equal("B", request.ClientReportedMacAddress);
    }

    [Fact]
    public void TryParse_DecodesFullWidthMacAddressField()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(80, 40).Fill((byte)'A');

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.Equal(new string('A', 40), request.ClientReportedMacAddress);
    }

    [Fact]
    public void TryParse_DoesNotApplyCharacterNameBusinessRules()
    {
        byte[] packet = CreatePacket();
        packet.AsSpan(24, 48).Clear();
        "ABC"u8.CopyTo(packet.AsSpan(24));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(GameCharacterCreationParseError.None, error);
        Assert.Equal("ABC", request.Name);
    }

    [Fact]
    public void TryParse_RejectsWrongPacketIdentifier()
    {
        byte[] packet = CreatePacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1002);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidPacketId, error);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(91)]
    [InlineData(92)]
    [InlineData(123)]
    [InlineData(125)]
    [InlineData(1025)]
    public void TryParse_RejectsUnsupportedPacketLengths(int length)
    {
        byte[] packet = new byte[length];
        WireFrameHeader.Write(packet, checked((ushort)length), GameCharacterCreationRequest.PacketId);

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameCharacterCreationRequest.TryParse(frame, out GameCharacterCreationRequest request,
            out GameCharacterCreationParseError error));

        Assert.Equal(default, request);
        Assert.Equal(GameCharacterCreationParseError.InvalidLength, error);
    }

    [Fact]
    public void TryParse_RejectsNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GameCharacterCreationRequest.TryParse(null!, out _, out _));
    }

    private static byte[] CreatePacket()
    {
        byte[] packet = new byte[GameCharacterCreationRequest.PacketLength];

        WireFrameHeader.Write(packet, checked((ushort)packet.Length), GameCharacterCreationRequest.PacketId);

        "NewHero"u8.CopyTo(packet.AsSpan(24, 48));

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(72), 1003);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(74), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(76), 0x12345678);

        "AABBCCDDEEFF"u8.CopyTo(packet.AsSpan(80, 40));

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
