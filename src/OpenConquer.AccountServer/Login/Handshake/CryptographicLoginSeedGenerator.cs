using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Generates account login seeds using the operating system cryptographic random number generator.
/// </summary>
internal sealed class CryptographicLoginSeedGenerator : ILoginSeedGenerator
{
    public uint GenerateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];

        RandomNumberGenerator.Fill(bytes);

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
