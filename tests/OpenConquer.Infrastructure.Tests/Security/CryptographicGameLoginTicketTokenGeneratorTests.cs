using OpenConquer.Infrastructure.Security;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class CryptographicGameLoginTicketTokenGeneratorTests
{
    private const int SampleCount = 4096;

    [Fact]
    public void GenerateSessionUid_RepeatedCallsAlwaysReturnNonzeroValues()
    {
        CryptographicGameLoginTicketTokenGenerator generator = new();

        for (int index = 0; index < SampleCount; index++)
        {
            uint sessionUid = generator.GenerateSessionUid();

            Assert.NotEqual(0u, sessionUid);
        }
    }

    [Fact]
    public void GenerateAuthenticationKey_RepeatedCallsAlwaysReturnNonzeroValues()
    {
        CryptographicGameLoginTicketTokenGenerator generator = new();

        for (int index = 0; index < SampleCount; index++)
        {
            uint authenticationKey = generator.GenerateAuthenticationKey();

            Assert.NotEqual(0u, authenticationKey);
        }
    }
}
