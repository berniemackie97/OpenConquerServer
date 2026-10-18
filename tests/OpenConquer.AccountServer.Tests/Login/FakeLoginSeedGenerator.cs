using OpenConquer.AccountServer.Login.Handshake;

namespace OpenConquer.AccountServer.Tests.Login;

internal sealed class FakeLoginSeedGenerator(uint seed) : ILoginSeedGenerator
{
    public int GenerateCount { get; private set; }

    public uint GenerateSeed()
    {
        GenerateCount++;

        return seed;
    }
}
