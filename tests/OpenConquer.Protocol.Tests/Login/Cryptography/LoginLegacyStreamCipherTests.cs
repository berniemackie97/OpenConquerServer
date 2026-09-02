using OpenConquer.Protocol.Login.Cryptography;

namespace OpenConquer.Protocol.Tests.Login.Cryptography;

public sealed class LoginLegacyStreamCipherTests
{
    [Fact]
    public void EncryptOutbound_MatchesVerifiedLoginSeedFrameVector()
    {
        LoginLegacyStreamCipher cipher = new();

        byte[] frame = [0x08, 0x00, 0x23, 0x04, 0x78, 0x56, 0x34, 0x12];

        cipher.EncryptOutbound(frame);

        Assert.Equal(Convert.FromHexString("C54869128E317C0F"), frame);
    }

    [Fact]
    public void DecryptInbound_MatchesVerifiedNativeEndpointVector()
    {
        LoginLegacyStreamCipher cipher = new();

        byte[] outboundProbe = [0x00];

        cipher.EncryptOutbound(outboundProbe);

        Assert.Equal(0x45, outboundProbe[0]);

        byte[] inboundCiphertext = Convert.FromHexString("D48487651720B0C3");

        cipher.DecryptInbound(inboundCiphertext);

        Assert.Equal(Convert.FromHexString("0800230478563412"), inboundCiphertext);
    }

    [Fact]
    public void EncryptOutbound_PreservesStreamStateAcrossCalls()
    {
        LoginLegacyStreamCipher cipher = new();

        byte[] frame = [0x08, 0x00, 0x23, 0x04, 0x78, 0x56, 0x34, 0x12];

        cipher.EncryptOutbound(frame.AsSpan(0, 3));

        cipher.EncryptOutbound(frame.AsSpan(3));

        Assert.Equal(Convert.FromHexString("C54869128E317C0F"), frame);
    }

    [Fact]
    public void EncryptOutbound_CarriesPositionIntoHighByte()
    {
        LoginLegacyStreamCipher cipher = new();

        byte[] buffer = new byte[258];

        cipher.EncryptOutbound(buffer);

        Assert.Equal(0x45, buffer[0]);

        Assert.Equal(0xE6, buffer[255]);

        Assert.Equal(0x68, buffer[256]);

        Assert.Equal(0x65, buffer[257]);
    }

    [Fact]
    public void EncryptOutbound_RepeatsKeystreamAfterFullPositionRange()
    {
        LoginLegacyStreamCipher cipher = new();

        byte[] fullPositionRange = new byte[ushort.MaxValue + 1];

        cipher.EncryptOutbound(fullPositionRange);

        byte[] nextByte = [0x00];

        cipher.EncryptOutbound(nextByte);

        Assert.Equal(fullPositionRange[0], nextByte[0]);

        Assert.Equal(0x45, nextByte[0]);
    }
}
