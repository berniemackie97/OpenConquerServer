using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginAccountMacAddressReportPacketTests
{
    [Fact]
    public void Layout_MatchesVerifiedNative1100()
    {
        Assert.Equal((ushort)1100, LoginAccountMacAddressReportPacket.PacketIdentifier);

        Assert.Equal(0, LoginAccountMacAddressReportPacket.SessionUidOffset);

        Assert.Equal(4, LoginAccountMacAddressReportPacket.MacAddressOffset);

        Assert.Equal(40, LoginAccountMacAddressReportPacket.MacAddressFieldLength);

        Assert.Equal(44, LoginAccountMacAddressReportPacket.TrailingBytesOffset);

        Assert.Equal(4, LoginAccountMacAddressReportPacket.TrailingBytesLength);

        Assert.Equal(48, LoginAccountMacAddressReportPacket.PayloadLength);

        Assert.Equal(52, LoginAccountMacAddressReportPacket.FrameLength);
    }

    [Fact]
    public void TryDecode_DecodesVerifiedNative1100Payload()
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x1122_3344);

        Encoding.ASCII.GetBytes(
            "001122AABBCC",
            payload.AsSpan(
                LoginAccountMacAddressReportPacket.MacAddressOffset,
                LoginAccountMacAddressReportPacket.MacAddressFieldLength
            )
        );

        bool decoded = LoginAccountMacAddressReportPacket.TryDecode(
            payload,
            out LoginAccountMacAddressReport? report
        );

        Assert.True(decoded);

        LoginAccountMacAddressReport actual = Assert.IsType<LoginAccountMacAddressReport>(report);

        Assert.Equal(0x1122_3344u, actual.SessionUid);

        Assert.Equal("001122AABBCC", actual.MacAddress);
    }

    [Fact]
    public void TryDecode_AcceptsNativeEmptyMacAddress()
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(payload, 7);

        bool decoded = LoginAccountMacAddressReportPacket.TryDecode(
            payload,
            out LoginAccountMacAddressReport? report
        );

        Assert.True(decoded);

        LoginAccountMacAddressReport actual = Assert.IsType<LoginAccountMacAddressReport>(report);

        Assert.Equal(7u, actual.SessionUid);

        Assert.Equal(string.Empty, actual.MacAddress);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(49)]
    public void TryDecode_RejectsIncorrectPayloadLength(int payloadLength)
    {
        bool decoded = LoginAccountMacAddressReportPacket.TryDecode(
            new byte[payloadLength],
            out LoginAccountMacAddressReport? report
        );

        Assert.False(decoded);

        Assert.Null(report);
    }

    [Fact]
    public void TryDecode_DoesNotInventValidationForUnspecifiedTrailingBytes()
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];

        payload
            .AsSpan(
                LoginAccountMacAddressReportPacket.TrailingBytesOffset,
                LoginAccountMacAddressReportPacket.TrailingBytesLength
            )
            .Fill(0xA5);

        bool decoded = LoginAccountMacAddressReportPacket.TryDecode(
            payload,
            out LoginAccountMacAddressReport? report
        );

        Assert.True(decoded);

        Assert.NotNull(report);
    }

    [Fact]
    public void TryDecode_DoesNotApplyAccountServerMacAddressPolicy()
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];

        Encoding.ASCII.GetBytes(
            "001122aabbcc",
            payload.AsSpan(
                LoginAccountMacAddressReportPacket.MacAddressOffset,
                LoginAccountMacAddressReportPacket.MacAddressFieldLength
            )
        );

        bool decoded = LoginAccountMacAddressReportPacket.TryDecode(
            payload,
            out LoginAccountMacAddressReport? report
        );

        Assert.True(decoded);

        Assert.Equal(
            "001122aabbcc",
            Assert.IsType<LoginAccountMacAddressReport>(report).MacAddress
        );
    }
}
