using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Game.Handshake;

public enum GameClientKeyExchangeDecodeStatus
{
    Success = 0,
    NeedMoreData,
    InvalidDeclaredLength,
    InvalidSignature,
    InvalidPayload,
}

public readonly record struct GameClientKeyExchangeDecodeResult(GameClientKeyExchangeDecodeStatus Status, int BytesConsumed, int RequiredBytes, string? ClientPublicKeyHex)
{
    public bool Succeeded => Status == GameClientKeyExchangeDecodeStatus.Success;
}

/// <summary>
/// Incrementally identifies and decodes the bootstrap-encrypted 5517 client
/// Diffie-Hellman response without consuming bytes from a following secured packet.
/// </summary>
public static class GameClientKeyExchangeResponseDecoder
{
    private const int PrefixLength = 7;
    private const int InitialProbeLength = PrefixLength + sizeof(int);
    private const int MinimumDeclaredLength = 31;
    private const int MaximumEnvelopeLength = 1024;
    private const int MaximumDeclaredLength = MaximumEnvelopeLength - PrefixLength;

    public static GameClientKeyExchangeDecodeResult Decode(ReadOnlySpan<byte> encryptedSource)
    {
        if (encryptedSource.Length < InitialProbeLength)
        {
            return NeedMoreData(InitialProbeLength);
        }

        Span<byte> probe = stackalloc byte[InitialProbeLength];
        GameHandshakeBootstrapCipher.Decrypt(encryptedSource[..InitialProbeLength], probe);

        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(probe[PrefixLength..]);

        if (declaredLength is < MinimumDeclaredLength or > MaximumDeclaredLength)
        {
            return Invalid(GameClientKeyExchangeDecodeStatus.InvalidDeclaredLength);
        }

        int totalLength = PrefixLength + declaredLength;

        if (encryptedSource.Length < totalLength)
        {
            return NeedMoreData(totalLength);
        }

        Span<byte> plaintext = stackalloc byte[totalLength];
        GameHandshakeBootstrapCipher.Decrypt(encryptedSource[..totalLength], plaintext);

        if (!plaintext[^GameWireProtocol.SignatureLength..].SequenceEqual(GameWireProtocol.ClientSignature))
        {
            return Invalid(GameClientKeyExchangeDecodeStatus.InvalidSignature);
        }

        ReadOnlySpan<byte> chunkList = plaintext.Slice(PrefixLength, declaredLength);

        if (!TryReadClientPublicKey(chunkList, out string? clientPublicKeyHex))
        {
            return Invalid(GameClientKeyExchangeDecodeStatus.InvalidPayload);
        }

        return new GameClientKeyExchangeDecodeResult(GameClientKeyExchangeDecodeStatus.Success, totalLength, totalLength, clientPublicKeyHex);
    }

    private static bool TryReadClientPublicKey(ReadOnlySpan<byte> chunkList, out string? clientPublicKeyHex)
    {
        clientPublicKeyHex = null;

        if (chunkList.Length < sizeof(int) + GameWireProtocol.SignatureLength)
        {
            return false;
        }

        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(chunkList);

        if (declaredLength != chunkList.Length)
        {
            return false;
        }

        int contentEnd = declaredLength - GameWireProtocol.SignatureLength;
        int offset = sizeof(int);

        if (!TryReadChunk(chunkList, contentEnd, ref offset, out ReadOnlySpan<byte> randomPadding) || randomPadding.IsEmpty ||
            !TryReadChunk(chunkList, contentEnd, ref offset, out ReadOnlySpan<byte> publicKey) || offset != contentEnd ||
            publicKey.IsEmpty || publicKey.Length > GameHandshakeExchange.PrimeHex.Length)
        {
            return false;
        }

        foreach (byte value in publicKey)
        {
            if (value is not (>= (byte)'0' and <= (byte)'9') and not (>= (byte)'A' and <= (byte)'F') and not (>= (byte)'a' and <= (byte)'f'))
            {
                return false;
            }
        }

        clientPublicKeyHex = Encoding.ASCII.GetString(publicKey);
        return true;
    }

    private static bool TryReadChunk(ReadOnlySpan<byte> source, int contentEnd, ref int offset, out ReadOnlySpan<byte> value)
    {
        value = default;

        if (offset > contentEnd - sizeof(int))
        {
            return false;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);

        if (length <= 0 || length > contentEnd - offset)
        {
            return false;
        }

        value = source.Slice(offset, length);
        offset += length;
        return true;
    }

    private static GameClientKeyExchangeDecodeResult NeedMoreData(int requiredBytes) =>
        new(GameClientKeyExchangeDecodeStatus.NeedMoreData, 0, requiredBytes, null);

    private static GameClientKeyExchangeDecodeResult Invalid(GameClientKeyExchangeDecodeStatus status) =>
        new(status, 0, 0, null);
}
