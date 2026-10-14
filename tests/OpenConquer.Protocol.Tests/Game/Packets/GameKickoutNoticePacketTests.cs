using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameKickoutNoticePacketTests
{
    [Fact]
    public void Packet_ExposesIdentifierAndFields()
    {
        GameKickoutNoticePacket packet = new(GameKickoutReason.OtherLogin, "Bye");

        Assert.Equal((ushort)1115, packet.PacketId);
        Assert.Equal(16, packet.PayloadLength);
        Assert.Equal(GameKickoutReason.OtherLogin, packet.Reason);
        Assert.Equal("Bye", packet.Text);
    }

    [Fact]
    public void Reasons_MatchClientValues()
    {
        Assert.Equal(0u, (uint)GameKickoutReason.OtherLogin);
        Assert.Equal(1u, (uint)GameKickoutReason.PasswordChanged);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesKickoutNoticeLayout()
    {
        GameKickoutNoticePacket packet = new(GameKickoutReason.PasswordChanged, "Bye");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x14, 0x00, 0x5B, 0x04,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            (byte)'B', (byte)'y', (byte)'e', 0x00,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void Constructor_AcceptsTextThatFillsTheClientBuffer()
    {
        GameKickoutNoticePacket packet = new(GameKickoutReason.OtherLogin, new string('x', GameKickoutNoticePacket.MaximumTextLength));

        Assert.Equal(12 + 255, packet.PayloadLength);
    }

    [Fact]
    public void Constructor_RejectsTextLongerThanTheClientBuffer()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new GameKickoutNoticePacket(GameKickoutReason.OtherLogin, new string('x', GameKickoutNoticePacket.MaximumTextLength + 1)));

        Assert.Equal("text", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullOrEmbeddedNullText()
    {
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => new GameKickoutNoticePacket(GameKickoutReason.OtherLogin, null!)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentException>(() => new GameKickoutNoticePacket(GameKickoutReason.OtherLogin, "B\0ye")).ParamName);
    }

    [Fact]
    public void Constructor_RejectsUnknownReason()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new GameKickoutNoticePacket((GameKickoutReason)2, "Bye"));

        Assert.Equal("reason", exception.ParamName);
    }
}
