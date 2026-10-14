using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Login.Admission;
using OpenConquer.GameServer.Realms;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Admission;

public sealed class GameLoginCapacityGateTests
{
    private const ushort LoginChannel = 0x835;

    [Fact]
    public void GetRefusal_AdmitsLoginsWhileTheRealmLoadIsNormal()
    {
        GameLoginCapacityGate gate = new(new FixedLoadSource(RealmLoad.Normal));

        Assert.Null(gate.GetRefusal());
    }

    [Theory]
    [InlineData(RealmLoad.Full, "The server is full. Please try again later.")]
    [InlineData(RealmLoad.Busy, "The server is busy. Please try again later.")]
    public void GetRefusal_ReturnsALoginChannelTalkWhileTheRealmCannotTakeLogins(RealmLoad load, string expectedMessage)
    {
        GameLoginCapacityGate gate = new(new FixedLoadSource(load));

        GameChatPacket refusal = Assert.IsType<GameChatPacket>(gate.GetRefusal());

        Assert.Equal(LoginChannel, refusal.Channel);
        Assert.Equal(expectedMessage, refusal.Message);
    }

    [Fact]
    public void Constructor_RejectsMissingLoadSource()
    {
        Assert.Throws<ArgumentNullException>(() => new GameLoginCapacityGate(null!));
    }

    private sealed class FixedLoadSource(RealmLoad load) : IRealmLoadSource
    {
        public RealmLoadSample Sample() => new(load, OnlineCount: 0);
    }
}
