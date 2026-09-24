using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameTalkPacket1004Tests
{
    [Fact]
    public void Packet_ExposesVerifiedIdentifierAndPayloadLength()
    {
        GameTalkPacket1004 packet = CreateAnswerOk();

        Assert.Equal((ushort)1004, packet.PacketId);
        Assert.Equal(34, packet.PayloadLength);
        Assert.Equal((uint)0, packet.Color);
        Assert.Equal((ushort)0x835, packet.Channel);
        Assert.Equal((ushort)0, packet.Style);
        Assert.Equal((uint)0, packet.Identity);
        Assert.Equal("", packet.Sender);
        Assert.Equal("", packet.Recipient);
        Assert.Equal("", packet.Suffix);
        Assert.Equal("ANSWER_OK", packet.Message);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedAnswerOkFrame()
    {
        GameTalkPacket1004 packet = CreateAnswerOk();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x26, 0x00, 0xEC, 0x03,
            0x00, 0x00, 0x00, 0x00,
            0x35, 0x08,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x04,
            0x00,
            0x00,
            0x00,
            0x09, (byte)'A', (byte)'N', (byte)'S', (byte)'W', (byte)'E', (byte)'R', (byte)'_', (byte)'O', (byte)'K',
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void Packet_UsesEncodedByteLengths()
    {
        GameTalkPacket1004 packet = new(
            color: 0,
            channel: 1,
            style: 0,
            identity: 0,
            sender: "€",
            recipient: "",
            suffix: "",
            message: "€");

        Assert.Equal(27, packet.PayloadLength);
    }

    [Fact]
    public void Constructor_AllowsVerifiedMaximumStringLengths()
    {
        GameTalkPacket1004 packet = new(
            color: 0,
            channel: 1,
            style: 0,
            identity: 0,
            sender: new string('A', GameTalkPacket1004.MaximumNameEncodedLength),
            recipient: new string('B', GameTalkPacket1004.MaximumNameEncodedLength),
            suffix: new string('C', GameTalkPacket1004.MaximumNameEncodedLength),
            message: new string('D', GameTalkPacket1004.MaximumMessageEncodedLength));

        Assert.Equal(325, packet.PayloadLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Constructor_RejectsNameFieldsAboveVerifiedMaximum(int field)
    {
        string oversized = new('A', GameTalkPacket1004.MaximumNameEncodedLength + 1);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => field switch
        {
            0 => new GameTalkPacket1004(0, 1, 0, 0, oversized, "", "", ""),
            1 => new GameTalkPacket1004(0, 1, 0, 0, "", oversized, "", ""),
            2 => new GameTalkPacket1004(0, 1, 0, 0, "", "", oversized, ""),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        });

        Assert.Equal(field switch
        {
            0 => "sender",
            1 => "recipient",
            2 => "suffix",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        }, exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsMessageAboveVerifiedMaximum()
    {
        string oversized = new('A', GameTalkPacket1004.MaximumMessageEncodedLength + 1);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameTalkPacket1004(0, 1, 0, 0, "", "", "", oversized));

        Assert.Equal("message", exception.ParamName);
    }

    private static GameTalkPacket1004 CreateAnswerOk()
    {
        return new GameTalkPacket1004(
            color: 0,
            channel: 0x835,
            style: 0,
            identity: 0,
            sender: "",
            recipient: "",
            suffix: "",
            message: "ANSWER_OK");
    }
}
