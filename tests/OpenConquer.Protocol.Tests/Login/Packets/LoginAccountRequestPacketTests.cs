using System.Text;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class LoginAccountRequestPacketTests
{
    private const uint LoginSeed = 0x0012_34AB;

    private const string AccountName = "testacc";

    private const string ServerName = "Conquer";

    private const string Password = "password1";

    private const string EncryptedCredential =
        "22DB42ACB82F421D" + "AEF13F7A611D5A03" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65";

    [Fact]
    public void Layout_MatchesVerifiedNative1060()
    {
        Assert.Equal((ushort)1060, LoginAccountRequestPacket.PacketIdentifier);

        Assert.Equal(0, LoginAccountRequestPacket.AccountNameOffset);

        Assert.Equal(128, LoginAccountRequestPacket.AccountNameLength);

        Assert.Equal(128, LoginAccountRequestPacket.CredentialFieldOffset);

        Assert.Equal(128, LoginAccountRequestPacket.CredentialFieldLength);

        Assert.Equal(32, LoginAccountRequestPacket.StandardCredentialTransformLength);

        Assert.Equal(256, LoginAccountRequestPacket.ServerNameOffset);

        Assert.Equal(16, LoginAccountRequestPacket.ServerNameLength);

        Assert.Equal(272, LoginAccountRequestPacket.PayloadLength);

        Assert.Equal(276, LoginAccountRequestPacket.FrameLength);
    }

    [Fact]
    public void TryDecodeStandard5517_DecodesVerifiedEnvelope()
    {
        byte[] payload = CreateValidPayload();

        byte[] original = payload.ToArray();

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed,
            out LoginAccountRequest? request
        );

        Assert.True(decoded);

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        Assert.Equal(AccountName, loginRequest.AccountName);

        Assert.Equal(ServerName, loginRequest.ServerName);

        Assert.Equal(Password, ReadPassword(loginRequest));

        Assert.Equal(original, payload);
    }

    [Theory]
    [InlineData(LoginAccountRequestPacket.PayloadLength - 1)]
    [InlineData(LoginAccountRequestPacket.PayloadLength + 1)]
    public void TryDecodeStandard5517_RejectsWrongPayloadLength(int payloadLength)
    {
        byte[] payload = new byte[payloadLength];

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed,
            out LoginAccountRequest? request
        );

        Assert.False(decoded);

        Assert.Null(request);
    }

    [Fact]
    public void TryDecodeStandard5517_IgnoresUnusedCredentialTail()
    {
        byte[] payload = CreateValidPayload();

        payload
            .AsSpan(
                LoginAccountRequestPacket.CredentialFieldOffset
                    + LoginAccountRequestPacket.StandardCredentialTransformLength,
                LoginAccountRequestPacket.CredentialFieldLength
                    - LoginAccountRequestPacket.StandardCredentialTransformLength
            )
            .Fill(0xA5);

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed,
            out LoginAccountRequest? request
        );

        Assert.True(decoded);

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        Assert.Equal(Password, ReadPassword(loginRequest));
    }

    [Fact]
    public void TryDecodeStandard5517_UsesAccountBytesOnlyThroughFirstNull()
    {
        byte[] payload = CreateValidPayload();

        payload[AccountName.Length + 1] = 0xE9;

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed,
            out LoginAccountRequest? request
        );

        Assert.True(decoded);

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        Assert.Equal(AccountName, loginRequest.AccountName);

        Assert.Equal(Password, ReadPassword(loginRequest));
    }

    [Fact]
    public void TryDecodeStandard5517_PreservesSignedNativeAccountByteSeed()
    {
        byte[] payload = new byte[LoginAccountRequestPacket.PayloadLength];

        payload[LoginAccountRequestPacket.AccountNameOffset] = 0xE9;

        Convert
            .FromHexString(
                "2DE21F21FFA061E0" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65"
            )
            .CopyTo(
                payload.AsSpan(
                    LoginAccountRequestPacket.CredentialFieldOffset,
                    LoginAccountRequestPacket.StandardCredentialTransformLength
                )
            );

        WriteAscii(
            ServerName,
            payload.AsSpan(
                LoginAccountRequestPacket.ServerNameOffset,
                LoginAccountRequestPacket.ServerNameLength
            )
        );

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed,
            out LoginAccountRequest? request
        );

        Assert.True(decoded);

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        Assert.Equal("é", loginRequest.AccountName);

        Assert.Equal("abc1", ReadPassword(loginRequest));
    }

    [Fact]
    public void TryDecodeStandard5517_WrongLoginSeedDoesNotRecoverPassword()
    {
        byte[] payload = CreateValidPayload();

        bool decoded = LoginAccountRequestPacket.TryDecodeStandard5517(
            payload,
            LoginSeed + 1,
            out LoginAccountRequest? request
        );

        Assert.True(decoded);

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        Assert.NotEqual(Password, ReadPassword(loginRequest));
    }

    [Fact]
    public void CopyPasswordTo_RejectsSmallDestinationWithoutMutation()
    {
        byte[] payload = CreateValidPayload();

        Assert.True(
            LoginAccountRequestPacket.TryDecodeStandard5517(
                payload,
                LoginSeed,
                out LoginAccountRequest? request
            )
        );

        using LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        char[] destination = new char[loginRequest.PasswordLength - 1];

        Array.Fill(destination, 'X');

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            loginRequest.CopyPasswordTo(destination)
        );

        Assert.Equal("destination", exception.ParamName);

        Assert.All(destination, value => Assert.Equal('X', value));
    }

    [Fact]
    public void Dispose_MakesPasswordUnavailableAndIsIdempotent()
    {
        byte[] payload = CreateValidPayload();

        Assert.True(
            LoginAccountRequestPacket.TryDecodeStandard5517(
                payload,
                LoginSeed,
                out LoginAccountRequest? request
            )
        );

        LoginAccountRequest loginRequest = Assert.IsType<LoginAccountRequest>(request);

        loginRequest.Dispose();

        char[] destination = new char[LoginAccountRequestPacket.StandardCredentialTransformLength];

        Assert.Throws<ObjectDisposedException>(() => loginRequest.CopyPasswordTo(destination));

        loginRequest.Dispose();
    }

    private static byte[] CreateValidPayload()
    {
        byte[] payload = new byte[LoginAccountRequestPacket.PayloadLength];

        WriteAscii(
            AccountName,
            payload.AsSpan(
                LoginAccountRequestPacket.AccountNameOffset,
                LoginAccountRequestPacket.AccountNameLength
            )
        );

        Convert
            .FromHexString(EncryptedCredential)
            .CopyTo(
                payload.AsSpan(
                    LoginAccountRequestPacket.CredentialFieldOffset,
                    LoginAccountRequestPacket.StandardCredentialTransformLength
                )
            );

        WriteAscii(
            ServerName,
            payload.AsSpan(
                LoginAccountRequestPacket.ServerNameOffset,
                LoginAccountRequestPacket.ServerNameLength
            )
        );

        return payload;
    }

    private static string ReadPassword(LoginAccountRequest request)
    {
        char[] password = new char[request.PasswordLength];

        request.CopyPasswordTo(password);

        return new string(password);
    }

    private static void WriteAscii(string value, Span<byte> destination)
    {
        Encoding.ASCII.GetBytes(value, destination);
    }
}
