using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Application.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

public sealed class CryptographicGameLoginTicketTokenGenerator : IGameLoginTicketTokenGenerator
{
    public uint GenerateSessionUid()
    {
        return GenerateNonzeroUInt32();
    }

    public uint GenerateAuthenticationKey()
    {
        return GenerateNonzeroUInt32();
    }

    private static uint GenerateNonzeroUInt32()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];

        try
        {
            uint value;

            do
            {
                RandomNumberGenerator.Fill(bytes);
                value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            } while (value == 0);

            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
