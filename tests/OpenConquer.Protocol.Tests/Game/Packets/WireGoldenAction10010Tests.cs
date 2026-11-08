using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class WireGoldenAction10010Tests
{
    [Fact]
    public void ActionEncoder_MatchesPinnedStaticDerivedFrame()
    {
        byte[] expected = WireGoldenFixture.ReadFrame(
            "10010-action.static-derived.json", GameAction.PacketIdentifier, "4b56d70dcb3e59d3ed0a1aa316c2d56603e6eb0dd0acb2dfe426ff0cf7d30a03");

        GameActionPacket packet = new(
            0x12345678, 0x23456789, 0x3456789a, 0x456789ab,
            108, 0x5678, 0x6789, 0x789a,
            0x89abcdef, 0x9abcdef0, 0xa5);
        byte[] actual = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        Assert.Equal(expected.Length, GameWireFrameEncoder.WriteFrame(packet, actual));
        Assert.Equal(expected, actual);
    }
}
