using System.Buffers;
using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameInboundChatTests
{
    [Theory]
    [InlineData(0x7D0, true, false, false)]
    [InlineData(0x7D7, true, false, false)]
    [InlineData(0x7D2, false, true, false)]
    [InlineData(0x836, false, false, true)]
    [InlineData(0x838, false, false, true)]
    [InlineData(0x835, false, false, false)]
    [InlineData(0x834, false, false, false)]
    [InlineData(0x7D5, false, false, false)]
    [InlineData(0x7D3, false, false, false)]
    public void ClassifiesOnlyEvidenceBackedWorldChannels(ushort channel, bool local, bool whisper, bool roleWord)
    {
        var request = new GameInboundChat(channel, "ALL", "hello");
        Assert.Equal(local, request.IsLocal);
        Assert.Equal(whisper, request.IsWhisper);
        Assert.Equal(roleWord, request.IsRoleWord);
        Assert.Equal(local || whisper || roleWord, request.IsSupported);
    }

    [Fact]
    public void ParsesOriginalWireWithUntrustedSenderAndIdentityIgnored()
    {
        using GameInboundFrame frame = Encode(new GameChatPacket(0x01020304, 0x7D7, 999, uint.MaxValue,
            "SYSTEM", "ALL", "forged", "Hello"));
        Assert.True(GameInboundChat.TryParse(frame, out GameInboundChat request));
        Assert.Equal((ushort)0x7D7, request.Channel);
        Assert.Equal("ALL", request.Recipient);
        Assert.Equal("Hello", request.Message);
    }

    [Fact]
    public void ParsesWindows1252AndNormalizesLineBreaks()
    {
        using GameInboundFrame frame = Encode(new GameChatPacket(uint.MaxValue, 0x7D2, 0, 0,
            "Player", "Targét", "", "hi\r\n€"));
        Assert.True(GameInboundChat.TryParse(frame, out GameInboundChat request));
        Assert.Equal("Targét", request.Recipient);
        Assert.Equal("hi  €", request.Message);
    }

    [Fact]
    public void RejectsWrongPacketId()
    {
        byte[] wire = Wire();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), 10010);
        using GameInboundFrame frame = Frame(wire);
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsCountOtherThanExactlyFour()
    {
        byte[] wire = Wire();
        wire[24] = 3;
        using GameInboundFrame frame = Frame(wire);
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsTruncatedStrings()
    {
        byte[] wire = Wire();
        wire[25] = byte.MaxValue;
        using GameInboundFrame frame = Frame(wire);
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsTrailingBytes()
    {
        byte[] wire = Wire();
        Array.Resize(ref wire, wire.Length + 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wire, checked((ushort)wire.Length));
        using GameInboundFrame frame = Frame(wire);
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsEmbeddedNul()
    {
        byte[] wire = Wire();
        // The first entry starts at +26 and is the untrusted sender.
        wire[26] = 0;
        using GameInboundFrame frame = Frame(wire);
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void RejectsEmptyRecipient()
    {
        using GameInboundFrame frame = Encode(new GameChatPacket(0, 0x7D7, 0, 0,
            "Player", "", "", "Hello"));
        Assert.False(GameInboundChat.TryParse(frame, out _));
    }

    [Fact]
    public void MaximumLengthFrameParsesWithoutTruncation()
    {
        using GameInboundFrame frame = Encode(new GameChatPacket(0, 0x7D7, 0, 0,
            new string('A', 15), new string('B', 15), new string('C', 15), new string('D', 255)));
        Assert.True(GameInboundChat.TryParse(frame, out GameInboundChat request));
        Assert.Equal(255, request.Message.Length);
    }

    private static byte[] Wire()
    {
        GameChatPacket packet = new(0, 0x7D7, 0, 0, "Player", "ALL", "", "Hello");
        byte[] bytes = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, bytes);
        return bytes;
    }

    private static GameInboundFrame Encode(GameChatPacket packet)
    {
        byte[] bytes = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, bytes);
        return Frame(bytes);
    }

    private static GameInboundFrame Frame(byte[] bytes)
    {
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        byte[] rented = ArrayPool<byte>.Shared.Rent(bytes.Length);
        bytes.CopyTo(rented, 0);
        return new GameInboundFrame(rented, header);
    }
}
