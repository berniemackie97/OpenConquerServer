using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace OpenConquer.Protocol.Game.Cryptography;

/// <summary>
/// Stateful CAST5 CFB-64 transform used by the 5517 game protocol.
/// One instance represents exactly one directional cipher stream.
/// </summary>
internal sealed class GameCast5Cfb64Cipher : IDisposable
{
    internal const int InitializationVectorLength = 8;

    private const int MaximumKeyLength = 16;
    private const int FeedbackMask = InitializationVectorLength - 1;

    private readonly Cast5Engine _engine = new();
    private readonly byte[] _feedbackRegister = new byte[InitializationVectorLength];
    private readonly byte[] _keystreamBlock = new byte[InitializationVectorLength];
    private readonly bool _encrypt;

    private int _feedbackPosition;
    private int _disposed;

    internal GameCast5Cfb64Cipher(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initializationVector, bool encrypt)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Key material must not be empty.", nameof(key));
        }

        if (initializationVector.Length != InitializationVectorLength)
        {
            throw new ArgumentException($"Initialization vector must contain exactly {InitializationVectorLength} bytes.", nameof(initializationVector));
        }

        byte[] normalizedKey = key[..Math.Min(key.Length, MaximumKeyLength)].ToArray();

        try
        {
            _engine.Init(true, new KeyParameter(normalizedKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(normalizedKey);
        }

        initializationVector.CopyTo(_feedbackRegister);
        _encrypt = encrypt;
    }

    internal void Transform(ReadOnlySpan<byte> input, Span<byte> output)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (output.Length < input.Length)
        {
            throw new ArgumentException("Output must be at least as long as input.", nameof(output));
        }

        if (input.Overlaps(output, out int elementOffset) && elementOffset != 0)
        {
            throw new ArgumentException("Input and output may be identical or non-overlapping, but not partially overlapping.", nameof(output));
        }

        for (int index = 0; index < input.Length; index++)
        {
            if (_feedbackPosition == 0)
            {
                _engine.ProcessBlock(_feedbackRegister, 0, _keystreamBlock, 0);
            }

            byte inputByte = input[index];

            if (_encrypt)
            {
                byte encryptedByte = (byte)(inputByte ^ _keystreamBlock[_feedbackPosition]);
                output[index] = encryptedByte;
                _feedbackRegister[_feedbackPosition] = encryptedByte;
            }
            else
            {
                output[index] = (byte)(inputByte ^ _keystreamBlock[_feedbackPosition]);
                _feedbackRegister[_feedbackPosition] = inputByte;
            }

            _feedbackPosition = (_feedbackPosition + 1) & FeedbackMask;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_feedbackRegister);
        CryptographicOperations.ZeroMemory(_keystreamBlock);
        _feedbackPosition = 0;
    }
}
