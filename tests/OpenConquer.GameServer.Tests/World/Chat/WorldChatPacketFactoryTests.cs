using OpenConquer.GameServer.World.Chat;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.World.Chat;

public sealed class WorldChatPacketFactoryTests
{
    [Fact]
    public void AuthenticatedSenderAndCanonicalRecipientAreUsedOnWire()
    {
        GameChatPacket packet = WorldChatPacketFactory.Create(1_000_200, "ActualPlayer", "TargetPlayer",
            new GameInboundChat(GameInboundChat.WhisperChannel, "targetplayer", "hello"));
        Assert.Equal(1_000_200u, packet.Identity);
        Assert.Equal("ActualPlayer", packet.Sender);
        Assert.Equal("TargetPlayer", packet.Recipient);
        Assert.Equal("", packet.Suffix);
        Assert.Equal(uint.MaxValue, packet.Color);
        Assert.Equal((ushort)0, packet.Style);
        Assert.Equal("hello", packet.Message);
    }

    [Fact]
    public void RoleWordIsLimitedToNativeClientCopyLength()
    {
        GameChatPacket packet = WorldChatPacketFactory.Create(1_000_200, "Speaker", "ALL",
            new GameInboundChat(GameInboundChat.RoleWordChannel, "ALL", new string('x', 65)));
        Assert.Equal(64, packet.Message.Length);
    }

    [Fact]
    public void OrdinaryChatIsNotTruncatedToRoleWordLength()
    {
        GameChatPacket packet = WorldChatPacketFactory.Create(1_000_200, "Speaker", "ALL",
            new GameInboundChat(GameInboundChat.LocalChannel, "ALL", new string('x', 255)));
        Assert.Equal(255, packet.Message.Length);
    }

    [Fact]
    public void CannotForgeReservedControlChannel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldChatPacketFactory.Create(1_000_200, "Speaker", "ALL",
            new GameInboundChat(0x835, "ALL", "NEW_ROLE")));
    }
}
