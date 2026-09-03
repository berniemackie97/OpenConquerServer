using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginAccountResourceVersionReportPacketTests
{
    [Fact]
    public void Layout_MatchesVerifiedNativeAccount1052()
    {
        Assert.Equal((ushort)1052, LoginAccountResourceVersionReportPacket.PacketIdentifier);

        Assert.Equal(0, LoginAccountResourceVersionReportPacket.SessionUidOffset);

        Assert.Equal(4, LoginAccountResourceVersionReportPacket.ResourceVersionOffset);

        Assert.Equal(8, LoginAccountResourceVersionReportPacket.ResourceNameOffset);

        Assert.Equal(16, LoginAccountResourceVersionReportPacket.ResourceNameFieldLength);

        Assert.Equal(24, LoginAccountResourceVersionReportPacket.PayloadLength);

        Assert.Equal(28, LoginAccountResourceVersionReportPacket.FrameLength);
    }

    [Fact]
    public void TryDecode_DecodesVerifiedNativeAccount1052Payload()
    {
        byte[] payload = new byte[LoginAccountResourceVersionReportPacket.PayloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x1122_3344);

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceVersionOffset), 5517);

        Encoding.ASCII.GetBytes("res.dat", payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceNameOffset, LoginAccountResourceVersionReportPacket.ResourceNameFieldLength));

        bool decoded = LoginAccountResourceVersionReportPacket.TryDecode(payload, out LoginAccountResourceVersionReport? report);

        Assert.True(decoded);

        LoginAccountResourceVersionReport actual = Assert.IsType<LoginAccountResourceVersionReport>(report);

        Assert.Equal(0x1122_3344u, actual.SessionUid);

        Assert.Equal(5517, actual.ResourceVersion);

        Assert.Equal("res.dat", actual.ResourceName);
    }

    [Fact]
    public void TryDecode_PreservesSignedResourceVersion()
    {
        byte[] payload = new byte[LoginAccountResourceVersionReportPacket.PayloadLength];

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceVersionOffset), -1);

        bool decoded = LoginAccountResourceVersionReportPacket.TryDecode(payload, out LoginAccountResourceVersionReport? report);

        Assert.True(decoded);

        Assert.Equal(-1, Assert.IsType<LoginAccountResourceVersionReport>(report).ResourceVersion);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(25)]
    public void TryDecode_RejectsIncorrectPayloadLength(int payloadLength)
    {
        bool decoded = LoginAccountResourceVersionReportPacket.TryDecode(new byte[payloadLength], out LoginAccountResourceVersionReport? report);

        Assert.False(decoded);

        Assert.Null(report);
    }

    [Fact]
    public void TryDecode_DoesNotApplyExpectedResourceNamePolicy()
    {
        byte[] payload = new byte[LoginAccountResourceVersionReportPacket.PayloadLength];

        Encoding.ASCII.GetBytes("patch.dat", payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceNameOffset, LoginAccountResourceVersionReportPacket.ResourceNameFieldLength));

        bool decoded = LoginAccountResourceVersionReportPacket.TryDecode(payload, out LoginAccountResourceVersionReport? report);

        Assert.True(decoded);

        Assert.Equal("patch.dat", Assert.IsType<LoginAccountResourceVersionReport>(report).ResourceName);
    }
}
