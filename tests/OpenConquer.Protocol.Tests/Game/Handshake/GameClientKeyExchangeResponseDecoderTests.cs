using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;

namespace OpenConquer.Protocol.Tests.Game.Handshake;

public sealed class GameClientKeyExchangeResponseDecoderTests
{
    [Fact]
    public void Decode_ParsesOpenConquerPublicCompatibleResponse()
    {
        byte[] response = BuildResponse("05");

        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(response);

        Assert.True(result.Succeeded);
        Assert.Equal(GameClientKeyExchangeDecodeStatus.Success, result.Status);
        Assert.Equal(response.Length, result.BytesConsumed);
        Assert.Equal(response.Length, result.RequiredBytes);
        Assert.Equal("05", result.ClientPublicKeyHex);
    }

    [Fact]
    public void Decode_DoesNotConsumeCoalescedSecuredPacketBytes()
    {
        byte[] response = BuildResponse("05");
        byte[] coalesced = [.. response, .. Enumerable.Repeat((byte)0xCC, 36)];

        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(coalesced);

        Assert.True(result.Succeeded);
        Assert.Equal(response.Length, result.BytesConsumed);
        Assert.Equal("05", result.ClientPublicKeyHex);
    }

    [Fact]
    public void Decode_ReportsExactRequirementAcrossFragmentation()
    {
        byte[] response = BuildResponse("05");

        GameClientKeyExchangeDecodeResult prefix = GameClientKeyExchangeResponseDecoder.Decode(response.AsSpan(0, 10));

        Assert.Equal(GameClientKeyExchangeDecodeStatus.NeedMoreData, prefix.Status);
        Assert.Equal(11, prefix.RequiredBytes);

        GameClientKeyExchangeDecodeResult probe = GameClientKeyExchangeResponseDecoder.Decode(response.AsSpan(0, 11));

        Assert.Equal(GameClientKeyExchangeDecodeStatus.NeedMoreData, probe.Status);
        Assert.Equal(response.Length, probe.RequiredBytes);

        GameClientKeyExchangeDecodeResult complete = GameClientKeyExchangeResponseDecoder.Decode(response);

        Assert.True(complete.Succeeded);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(1018)]
    public void Decode_RejectsInvalidDeclaredLength(int declaredLength)
    {
        byte[] plaintext = new byte[11];
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(7), declaredLength);
        byte[] encrypted = new byte[plaintext.Length];

        GameHandshakeBootstrapCipher.Encrypt(plaintext, encrypted);

        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(encrypted);

        Assert.Equal(GameClientKeyExchangeDecodeStatus.InvalidDeclaredLength, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Decode_RejectsInvalidSignature()
    {
        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(BuildResponse("05", validSignature: false));

        Assert.Equal(GameClientKeyExchangeDecodeStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Decode_RejectsUnexpectedTrailingChunk()
    {
        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(BuildResponse("05", appendUnexpectedChunk: true));

        Assert.Equal(GameClientKeyExchangeDecodeStatus.InvalidPayload, result.Status);
    }

    [Fact]
    public void Decode_RejectsNonHexadecimalPublicKey()
    {
        GameClientKeyExchangeDecodeResult result = GameClientKeyExchangeResponseDecoder.Decode(BuildResponse("NOTHEX"));

        Assert.Equal(GameClientKeyExchangeDecodeStatus.InvalidPayload, result.Status);
    }

    private static byte[] BuildResponse(string publicKeyHex, bool validSignature = true, bool appendUnexpectedChunk = false)
    {
        byte[] publicKey = Encoding.ASCII.GetBytes(publicKeyHex);
        byte[] randomPadding = Enumerable.Range(1, 12).Select(static value => (byte)value).ToArray();

        int chunkListLength = sizeof(int) + sizeof(int) + randomPadding.Length + sizeof(int) + publicKey.Length +
                              (appendUnexpectedChunk ? sizeof(int) + 1 : 0);
        int declaredLength = chunkListLength + GameWireProtocol.SignatureLength;
        byte[] plaintext = new byte[7 + declaredLength];

        for (int index = 0; index < 7; index++)
        {
            plaintext[index] = (byte)(0xA0 + index);
        }

        int offset = 7;
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset), declaredLength);
        offset += sizeof(int);

        WriteChunk(plaintext, ref offset, randomPadding);
        WriteChunk(plaintext, ref offset, publicKey);

        if (appendUnexpectedChunk)
        {
            WriteChunk(plaintext, ref offset, [0x01]);
        }

        (validSignature ? GameWireProtocol.ClientSignature : "NotValid"u8).CopyTo(plaintext.AsSpan(offset));

        byte[] encrypted = new byte[plaintext.Length];
        GameHandshakeBootstrapCipher.Encrypt(plaintext, encrypted);
        return encrypted;
    }

    private static void WriteChunk(Span<byte> destination, ref int offset, ReadOnlySpan<byte> value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], value.Length);
        offset += sizeof(int);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }
}
