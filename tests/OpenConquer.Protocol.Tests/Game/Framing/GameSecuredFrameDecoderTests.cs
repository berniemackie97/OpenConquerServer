using System.Buffers.Binary;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Framing;

public sealed class GameSecuredFrameDecoderTests
{
    private static ReadOnlySpan<byte> Key => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Fact]
    public void Decode_DecryptsAndParsesNative1052LoginProof()
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] packet = BuildLoginProof();
        byte[] encrypted = EncryptClientFrames(Key, inboundInitializationVector, packet);

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameSecuredFrameDecodeResult result = decoder.Decode(encrypted);

        Assert.True(result.Succeeded);
        Assert.Equal(encrypted.Length, result.BytesConsumed);

        using GameInboundFrame frame = result.Frame!;

        Assert.Equal(packet, frame.Packet.ToArray());
        Assert.True(GameLoginProof1052.TryParse(frame, out GameLoginProof1052 proof, out GameLoginProofParseError error));
        Assert.Equal(GameLoginProofParseError.None, error);
        Assert.Equal(0x10203040U, proof.SessionUid);
        Assert.Equal(0x50607080U, proof.AuthenticationKey);
        Assert.Equal(GameLoginProof1052.ExpectedMode, proof.Mode);
        Assert.Equal((ushort)0x6E45, proof.LocaleTag);
        Assert.Equal(0x0000_6655_4433_2211UL, proof.HardwareAddress);
        Assert.Equal(5517, proof.ResourceVersion);
    }

    [Fact]
    public void Decode_ConsumesExactlyOneFrameFromCoalescedCiphertext()
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] firstPacket = BuildPacket(1052, [0x11, 0x22, 0x33]);
        byte[] secondPacket = BuildPacket(1004, [0x44, 0x55, 0x66, 0x77]);
        byte[] encrypted = EncryptClientFrames(Key, inboundInitializationVector, firstPacket, secondPacket);

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameSecuredFrameDecodeResult first = decoder.Decode(encrypted);

        Assert.True(first.Succeeded);
        Assert.Equal(firstPacket.Length + GameWireProtocol.SignatureLength, first.BytesConsumed);

        using (GameInboundFrame frame = first.Frame!)
        {
            Assert.Equal(firstPacket, frame.Packet.ToArray());
        }

        GameSecuredFrameDecodeResult second = decoder.Decode(encrypted.AsSpan(first.BytesConsumed));

        Assert.True(second.Succeeded);
        Assert.Equal(secondPacket.Length + GameWireProtocol.SignatureLength, second.BytesConsumed);

        using GameInboundFrame secondFrame = second.Frame!;

        Assert.Equal(secondPacket, secondFrame.Packet.ToArray());
    }

    [Fact]
    public void Decode_PreservesCipherStateAcrossSingleByteFragmentation()
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] packet = BuildPacket(1052, Enumerable.Range(0, 73).Select(static value => (byte)value).ToArray());
        byte[] encrypted = EncryptClientFrames(Key, inboundInitializationVector, packet);

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameInboundFrame? decodedFrame = null;

        for (int index = 0; index < encrypted.Length; index++)
        {
            GameSecuredFrameDecodeResult result = decoder.Decode(encrypted.AsSpan(index, 1));

            Assert.Equal(1, result.BytesConsumed);

            if (index < encrypted.Length - 1)
            {
                Assert.Equal(GameSecuredFrameDecodeStatus.NeedMoreData, result.Status);
            }
            else
            {
                Assert.True(result.Succeeded);
                decodedFrame = result.Frame;
            }
        }

        using (decodedFrame)
        {
            Assert.NotNull(decodedFrame);
            Assert.Equal(packet, decodedFrame.Packet.ToArray());
        }
    }

    [Fact]
    public void Decode_AllowsNativeMaximumPacketLength()
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] packet = new byte[GameWireProtocol.MaximumPacketLength];
        WireFrameHeader.Write(packet, GameWireProtocol.MaximumPacketLength, 1052);
        byte[] encrypted = EncryptClientFrames(Key, inboundInitializationVector, packet);

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameSecuredFrameDecodeResult result = decoder.Decode(encrypted);

        Assert.True(result.Succeeded);
        Assert.Equal(GameWireProtocol.MaximumWireFrameLength, result.BytesConsumed);

        using GameInboundFrame frame = result.Frame!;

        Assert.Equal(GameWireProtocol.MaximumPacketLength, frame.Packet.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(0x401)]
    public void Decode_RejectsInvalidPacketLengthAndBecomesTerminal(int packetLength)
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] header = new byte[WireFrameHeader.Size];

        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)packetLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(sizeof(ushort)), 1052);

        byte[] encrypted = new byte[header.Length];

        using (GameCast5Cfb64Cipher clientCipher = new(Key, inboundInitializationVector, encrypt: true))
        {
            clientCipher.Transform(header, encrypted);
        }

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameSecuredFrameDecodeResult result = decoder.Decode(encrypted);

        Assert.Equal(GameSecuredFrameDecodeStatus.InvalidPacketLength, result.Status);
        Assert.Equal(WireFrameHeader.Size, result.BytesConsumed);
        Assert.Null(result.Frame);

        Assert.Throws<InvalidOperationException>(() => decoder.Decode([0x00]));
    }

    [Fact]
    public void Decode_RejectsWrongClientSignatureAndBecomesTerminal()
    {
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] packet = BuildPacket(1052, [0x01, 0x02]);
        byte[] plaintext = new byte[packet.Length + GameWireProtocol.SignatureLength];

        packet.CopyTo(plaintext, 0);
        "NotValid"u8.CopyTo(plaintext.AsSpan(packet.Length));

        byte[] encrypted = new byte[plaintext.Length];

        using (GameCast5Cfb64Cipher clientCipher = new(Key, inboundInitializationVector, encrypt: true))
        {
            clientCipher.Transform(plaintext, encrypted);
        }

        using GameSessionCipher sessionCipher = new(Key, inboundInitializationVector, new byte[GameCast5Cfb64Cipher.InitializationVectorLength]);
        using GameSecuredFrameDecoder decoder = new(sessionCipher);

        GameSecuredFrameDecodeResult result = decoder.Decode(encrypted);

        Assert.Equal(GameSecuredFrameDecodeStatus.InvalidSignature, result.Status);
        Assert.Equal(encrypted.Length, result.BytesConsumed);
        Assert.Null(result.Frame);

        Assert.Throws<InvalidOperationException>(() => decoder.Decode([0x00]));
    }

    [Fact]
    public void Dispose_RejectsFurtherDecoding()
    {
        using GameSessionCipher sessionCipher = new(Key, new byte[8], new byte[8]);
        GameSecuredFrameDecoder decoder = new(sessionCipher);

        decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => decoder.Decode([0x00]));
    }

    private static byte[] BuildLoginProof()
    {
        byte[] packet = new byte[GameLoginProof1052.PacketLength];

        WireFrameHeader.Write(packet, GameLoginProof1052.PacketLength, GameLoginProof1052.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 0x10203040);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0x50607080);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), GameLoginProof1052.ExpectedMode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), 0x6E45);

        packet[16] = 0x11;
        packet[17] = 0x22;
        packet[18] = 0x33;
        packet[19] = 0x44;
        packet[20] = 0x55;
        packet[21] = 0x66;

        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24), 5517);
        return packet;
    }

    private static byte[] BuildPacket(ushort packetId, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[WireFrameHeader.Size + payload.Length];

        WireFrameHeader.Write(packet, (ushort)packet.Length, packetId);
        payload.CopyTo(packet.AsSpan(WireFrameHeader.Size));

        return packet;
    }

    private static byte[] EncryptClientFrames(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initializationVector, params byte[][] packets)
    {
        int length = packets.Sum(static packet => packet.Length + GameWireProtocol.SignatureLength);
        byte[] plaintext = new byte[length];
        int offset = 0;

        foreach (byte[] packet in packets)
        {
            packet.CopyTo(plaintext, offset);
            offset += packet.Length;

            GameWireProtocol.ClientSignature.CopyTo(plaintext.AsSpan(offset));
            offset += GameWireProtocol.SignatureLength;
        }

        byte[] encrypted = new byte[plaintext.Length];

        using GameCast5Cfb64Cipher cipher = new(key, initializationVector, encrypt: true);
        cipher.Transform(plaintext, encrypted);

        return encrypted;
    }
}
