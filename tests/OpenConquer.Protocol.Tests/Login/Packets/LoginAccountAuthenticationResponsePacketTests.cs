using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginAccountAuthenticationResponsePacketTests
{
    [Fact]
    public void FailureCodes_MatchVerifiedNativeValues()
    {
        Assert.Equal(1u, (uint)LoginAccountAuthenticationFailureCode.InvalidCredentials);

        Assert.Equal(12u, (uint)LoginAccountAuthenticationFailureCode.Banned);

        Assert.Equal(57u, (uint)LoginAccountAuthenticationFailureCode.InvalidAccount);
    }

    [Fact]
    public void Success_ExposesVerifiedPacketState()
    {
        LoginAccountAuthenticationResponsePacket packet = LoginAccountAuthenticationResponsePacket.Success(sessionUid: 0x1122_3344, authenticationKey: 0x5566_7788, gameServerPort: 5816, additionalSessionField: 0x99AA_BBCC, gameServerIp: "127.0.0.1");

        Assert.Equal((ushort)1055, packet.PacketId);

        Assert.Equal(32, packet.PayloadLength);

        Assert.True(packet.IsSuccess);

        Assert.Equal(0x1122_3344u, packet.SessionUid);

        Assert.Equal(0x5566_7788u, packet.AuthenticationKeyOrFailureCode);

        Assert.Equal(5816u, packet.GameServerPort);

        Assert.Equal(0x99AA_BBCCu, packet.AdditionalSessionField);

        Assert.Equal("127.0.0.1", packet.GameServerIp);
    }

    [Fact]
    public void Failure_EnforcesVerifiedFailureState()
    {
        LoginAccountAuthenticationResponsePacket packet = LoginAccountAuthenticationResponsePacket.Failure(LoginAccountAuthenticationFailureCode.Banned, gameServerPort: 5816, gameServerIp: "127.0.0.1");

        Assert.False(packet.IsSuccess);

        Assert.Equal(0u, packet.SessionUid);

        Assert.Equal(12u, packet.AuthenticationKeyOrFailureCode);

        Assert.Equal(5816u, packet.GameServerPort);

        Assert.Equal(0u, packet.AdditionalSessionField);

        Assert.Equal("127.0.0.1", packet.GameServerIp);
    }

    [Fact]
    public void Success_RejectsZeroSessionUid()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => LoginAccountAuthenticationResponsePacket.Success(sessionUid: 0, authenticationKey: 1, gameServerPort: 5816, additionalSessionField: 1, gameServerIp: "127.0.0.1"));

        Assert.Equal("sessionUid", exception.ParamName);
    }

    [Fact]
    public void Failure_RejectsUnknownFailureCode()
    {
        LoginAccountAuthenticationFailureCode invalid = (LoginAccountAuthenticationFailureCode)uint.MaxValue;

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => LoginAccountAuthenticationResponsePacket.Failure(invalid, gameServerPort: 5816, gameServerIp: "127.0.0.1"));

        Assert.Equal("failureCode", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("::1")]
    public void Factories_RejectInvalidGameServerIp(string gameServerIp)
    {
        Assert.Throws<ArgumentException>(() => LoginAccountAuthenticationResponsePacket.Success(sessionUid: 1, authenticationKey: 2, gameServerPort: 5816, additionalSessionField: 3, gameServerIp));

        Assert.Throws<ArgumentException>(() => LoginAccountAuthenticationResponsePacket.Failure(LoginAccountAuthenticationFailureCode.InvalidCredentials, gameServerPort: 5816, gameServerIp));
    }

    [Fact]
    public void WireFrameEncoder_WritesVerifiedSuccess1055Vector()
    {
        LoginAccountAuthenticationResponsePacket packet = LoginAccountAuthenticationResponsePacket.Success(sessionUid: 0x1122_3344, authenticationKey: 0x5566_7788, gameServerPort: 5816, additionalSessionField: 0x99AA_BBCC, gameServerIp: "127.0.0.1");

        byte[] destination = new byte[WireFrameHeader.Size + LoginAccountAuthenticationResponsePacket.PayloadSize];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x24,
            0x00,
            0x1F,
            0x04,
            0x44,
            0x33,
            0x22,
            0x11,
            0x88,
            0x77,
            0x66,
            0x55,
            0xB8,
            0x16,
            0x00,
            0x00,
            0xCC,
            0xBB,
            0xAA,
            0x99,
            (byte)'1',
            (byte)'2',
            (byte)'7',
            (byte)'.',
            (byte)'0',
            (byte)'.',
            (byte)'0',
            (byte)'.',
            (byte)'1',
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
        ];

        Assert.Equal(36, written);

        Assert.Equal(expected, destination);
    }

    [Fact]
    public void WireFrameEncoder_WritesVerifiedFailure1055Vector()
    {
        LoginAccountAuthenticationResponsePacket packet = LoginAccountAuthenticationResponsePacket.Failure(LoginAccountAuthenticationFailureCode.InvalidCredentials, gameServerPort: 5816, gameServerIp: "127.0.0.1");

        byte[] destination = new byte[WireFrameHeader.Size + LoginAccountAuthenticationResponsePacket.PayloadSize];

        int written = WireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x24,
            0x00,
            0x1F,
            0x04,
            0x00,
            0x00,
            0x00,
            0x00,
            0x01,
            0x00,
            0x00,
            0x00,
            0xB8,
            0x16,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            (byte)'1',
            (byte)'2',
            (byte)'7',
            (byte)'.',
            (byte)'0',
            (byte)'.',
            (byte)'0',
            (byte)'.',
            (byte)'1',
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
        ];

        Assert.Equal(36, written);

        Assert.Equal(expected, destination);
    }
}
