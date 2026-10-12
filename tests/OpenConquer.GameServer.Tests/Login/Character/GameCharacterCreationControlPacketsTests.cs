using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Character;

public sealed class GameCharacterCreationControlPacketsTests
{
    [Fact]
    public void NewRole_IsNativeLoginChannelControlTalk()
    {
        AssertControl(GameCharacterCreationControlPackets.NewRole, 0x835, "NEW_ROLE");
    }

    [Fact]
    public void Accepted_IsNativeCreationChannelControlTalk()
    {
        AssertControl(GameCharacterCreationControlPackets.Accepted, 0x834, "ANSWER_OK");
    }

    [Fact]
    public void CreationSuccess_DoesNotReuseExistingCharacterLoginControlChannel()
    {
        Assert.Equal("ANSWER_OK", ExistingCharacterBootstrapPacketFactory.Accepted.Message);
        Assert.Equal((ushort)0x835, ExistingCharacterBootstrapPacketFactory.Accepted.Channel);
        Assert.Equal((ushort)0x834, GameCharacterCreationControlPackets.Accepted.Channel);
        Assert.NotSame(ExistingCharacterBootstrapPacketFactory.Accepted, GameCharacterCreationControlPackets.Accepted);
    }

    private static void AssertControl(GameChatPacket packet, ushort channel, string message)
    {
        Assert.Equal(GameChatPacket.PacketIdentifier, packet.PacketId);
        Assert.Equal((uint)0, packet.Color);
        Assert.Equal(channel, packet.Channel);
        Assert.Equal((ushort)0, packet.Style);
        Assert.Equal((uint)0, packet.Identity);
        Assert.Empty(packet.Sender);
        Assert.Empty(packet.Recipient);
        Assert.Empty(packet.Suffix);
        Assert.Equal(message, packet.Message);
    }
}
