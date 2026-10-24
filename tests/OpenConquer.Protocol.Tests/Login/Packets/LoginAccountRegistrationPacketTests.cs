using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginAccountRegistrationPacketTests
{
    private const uint LoginSeed = 0x0012_34AB;
    private const string AccountName = "testacc";
    private const string Password = "password1";
    private const string EncryptedCredential =
        "22DB42ACB82F421D" + "AEF13F7A611D5A03" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65";

    [Fact]
    public void RequestLayout_SharesTheLoginEnvelopeWithoutServerName()
    {
        Assert.Equal((ushort)0xF001, LoginAccountRegistrationRequestPacket.PacketIdentifier);
        Assert.Equal(LoginAccountRequestPacket.AccountNameOffset, LoginAccountRegistrationRequestPacket.AccountNameOffset);
        Assert.Equal(LoginAccountRequestPacket.CredentialFieldOffset, LoginAccountRegistrationRequestPacket.CredentialFieldOffset);
        Assert.Equal(256, LoginAccountRegistrationRequestPacket.PayloadLength);
        Assert.Equal(260, LoginAccountRegistrationRequestPacket.FrameLength);
    }

    [Fact]
    public void TryDecode_RecoversNameAndPasswordThroughTheLoginTransform()
    {
        byte[] payload = CreatePayload();
        byte[] original = payload.ToArray();

        Assert.True(LoginAccountRegistrationRequestPacket.TryDecode(payload, LoginSeed, out LoginAccountRegistrationRequest? decoded));

        using LoginAccountRegistrationRequest request = Assert.IsType<LoginAccountRegistrationRequest>(decoded);
        char[] password = new char[request.PasswordLength];
        request.CopyPasswordTo(password);

        Assert.Equal(AccountName, request.AccountName);
        Assert.Equal(Password, new string(password));
        Assert.Equal(original, payload);
    }

    [Theory]
    [InlineData(LoginAccountRegistrationRequestPacket.PayloadLength - 1)]
    [InlineData(LoginAccountRegistrationRequestPacket.PayloadLength + 1)]
    [InlineData(LoginAccountRequestPacket.PayloadLength)]
    public void TryDecode_RejectsWrongPayloadLength(int length)
    {
        Assert.False(LoginAccountRegistrationRequestPacket.TryDecode(new byte[length], LoginSeed, out LoginAccountRegistrationRequest? request));
        Assert.Null(request);
    }

    [Fact]
    public void DisposedRequest_RefusesPasswordCopy()
    {
        Assert.True(LoginAccountRegistrationRequestPacket.TryDecode(CreatePayload(), LoginSeed, out LoginAccountRegistrationRequest? request));
        request!.Dispose();

        Assert.Throws<ObjectDisposedException>(() => request.CopyPasswordTo(new char[32]));
    }

    [Theory]
    [InlineData(LoginAccountRegistrationResultCode.Success)]
    [InlineData(LoginAccountRegistrationResultCode.InvalidRequest)]
    [InlineData(LoginAccountRegistrationResultCode.AccountNameTaken)]
    [InlineData(LoginAccountRegistrationResultCode.Failed)]
    [InlineData(LoginAccountRegistrationResultCode.Unavailable)]
    public void Response_EncodesEightByteFrame(LoginAccountRegistrationResultCode code)
    {
        LoginAccountRegistrationResponsePacket packet = new(code);
        byte[] frame = new byte[WireFrameHeader.Size + packet.PayloadLength];

        Assert.Equal(8, WireFrameEncoder.WriteFrame(packet, frame));

        Assert.Equal(8, frame.Length);
        Assert.Equal((ushort)8, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.Equal((ushort)0xF002, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
        Assert.Equal((uint)code, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)));
    }

    [Fact]
    public void Response_RejectsUnknownCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoginAccountRegistrationResponsePacket((LoginAccountRegistrationResultCode)99));
    }

    private static byte[] CreatePayload()
    {
        byte[] payload = new byte[LoginAccountRegistrationRequestPacket.PayloadLength];
        Encoding.ASCII.GetBytes(AccountName, payload.AsSpan(0, LoginAccountRegistrationRequestPacket.AccountNameLength));
        Convert.FromHexString(EncryptedCredential).CopyTo(payload.AsSpan(LoginAccountRegistrationRequestPacket.CredentialFieldOffset));
        return payload;
    }
}
