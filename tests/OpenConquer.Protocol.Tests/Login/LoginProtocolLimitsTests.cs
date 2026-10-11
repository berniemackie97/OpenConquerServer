using OpenConquer.Protocol.Login;

namespace OpenConquer.Protocol.Tests.Login;

public sealed class LoginProtocolLimitsTests
{
    [Fact]
    public void MaximumFrameLength_MatchesLargestAccountLoginFrame()
    {
        Assert.Equal(524, LoginProtocolLimits.MaximumFrameLength);
    }
}
