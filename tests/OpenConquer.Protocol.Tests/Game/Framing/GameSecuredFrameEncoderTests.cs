using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Tests.Game.Framing;

public sealed class GameSecuredFrameEncoderTests
{
    private static ReadOnlySpan<byte> Key => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Fact]
    public void WriteFrame_AppendsAndEncryptsNativeServerSignature()
    {
        byte[] outboundInitializationVector = Convert.FromHexString("1112131415161718");
        byte[] packet = BuildPacket(1004, [0x11, 0x22, 0x33]);
        byte[] encrypted = new byte[packet.Length + GameWireProtocol.SignatureLength];

        using GameSessionCipher sessionCipher = new(Key, new byte[8], outboundInitializationVector);

        int written = GameSecuredFrameEncoder.WriteFrame(sessionCipher, packet, encrypted);

        Assert.Equal(encrypted.Length, written);

        byte[] plaintext = new byte[encrypted.Length];

        using (GameCast5Cfb64Cipher clientCipher = new(Key, outboundInitializationVector, encrypt: false))
        {
            clientCipher.Transform(encrypted, plaintext);
        }

        Assert.Equal(packet, plaintext[..packet.Length]);
        Assert.Equal(GameWireProtocol.ServerSignature.ToArray(), plaintext[packet.Length..]);
    }

    [Fact]
    public void WriteFrame_PreservesOutboundStateAcrossFrames()
    {
        byte[] outboundInitializationVector = Convert.FromHexString("1112131415161718");
        byte[] firstPacket = BuildPacket(1004, [0x01, 0x02]);
        byte[] secondPacket = BuildPacket(1009, [0x03, 0x04, 0x05]);

        byte[] firstEncrypted = new byte[GameSecuredFrameEncoder.GetWireLength(firstPacket)];
        byte[] secondEncrypted = new byte[GameSecuredFrameEncoder.GetWireLength(secondPacket)];

        using GameSessionCipher sessionCipher = new(Key, new byte[8], outboundInitializationVector);

        GameSecuredFrameEncoder.WriteFrame(sessionCipher, firstPacket, firstEncrypted);
        GameSecuredFrameEncoder.WriteFrame(sessionCipher, secondPacket, secondEncrypted);

        byte[] encrypted = [.. firstEncrypted, .. secondEncrypted];
        byte[] plaintext = new byte[encrypted.Length];

        using (GameCast5Cfb64Cipher clientCipher = new(Key, outboundInitializationVector, encrypt: false))
        {
            clientCipher.Transform(encrypted, plaintext);
        }

        byte[] expected =
        [
            .. firstPacket,
            .. GameWireProtocol.ServerSignature,
            .. secondPacket,
            .. GameWireProtocol.ServerSignature,
        ];

        Assert.Equal(expected, plaintext);
    }

    [Fact]
    public void GetWireLength_AllowsNativeMaximumPacketLength()
    {
        byte[] packet = new byte[GameWireProtocol.MaximumPacketLength];
        WireFrameHeader.Write(packet, GameWireProtocol.MaximumPacketLength, 1052);

        Assert.Equal(GameWireProtocol.MaximumWireFrameLength, GameSecuredFrameEncoder.GetWireLength(packet));
    }

    [Fact]
    public void WriteFrame_RejectsInvalidPacketBeforeAdvancingCipherState()
    {
        byte[] outboundInitializationVector = Convert.FromHexString("1112131415161718");
        byte[] invalidPacket = [0x05, 0x00, 0x01, 0x00];
        byte[] invalidDestination = Enumerable.Repeat((byte)0xCC, 16).ToArray();
        byte[] validPacket = BuildPacket(1004, [0xAA]);
        byte[] actual = new byte[GameSecuredFrameEncoder.GetWireLength(validPacket)];
        byte[] expected = new byte[actual.Length];

        using GameSessionCipher tested = new(Key, new byte[8], outboundInitializationVector);
        using GameSessionCipher baseline = new(Key, new byte[8], outboundInitializationVector);

        Assert.Throws<ArgumentException>(() => GameSecuredFrameEncoder.WriteFrame(tested, invalidPacket, invalidDestination));
        Assert.All(invalidDestination, value => Assert.Equal(0xCC, value));

        GameSecuredFrameEncoder.WriteFrame(tested, validPacket, actual);
        GameSecuredFrameEncoder.WriteFrame(baseline, validPacket, expected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteFrame_RejectsSmallDestinationBeforeAdvancingCipherState()
    {
        byte[] outboundInitializationVector = Convert.FromHexString("1112131415161718");
        byte[] packet = BuildPacket(1004, [0xAA]);
        int wireLength = GameSecuredFrameEncoder.GetWireLength(packet);
        byte[] smallDestination = Enumerable.Repeat((byte)0xCC, wireLength - 1).ToArray();
        byte[] actual = new byte[wireLength];
        byte[] expected = new byte[wireLength];

        using GameSessionCipher tested = new(Key, new byte[8], outboundInitializationVector);
        using GameSessionCipher baseline = new(Key, new byte[8], outboundInitializationVector);

        Assert.Throws<ArgumentException>(() => GameSecuredFrameEncoder.WriteFrame(tested, packet, smallDestination));
        Assert.All(smallDestination, value => Assert.Equal(0xCC, value));

        GameSecuredFrameEncoder.WriteFrame(tested, packet, actual);
        GameSecuredFrameEncoder.WriteFrame(baseline, packet, expected);

        Assert.Equal(expected, actual);
    }

    private static byte[] BuildPacket(ushort packetId, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[WireFrameHeader.Size + payload.Length];

        WireFrameHeader.Write(packet, (ushort)packet.Length, packetId);
        payload.CopyTo(packet.AsSpan(WireFrameHeader.Size));

        return packet;
    }
}
