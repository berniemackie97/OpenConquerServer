using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin.Redemption;

namespace OpenConquer.GameServer.Tests.Login;

internal sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
{
    public GameLoginTicketIdentity? Result { get; init; }
    public uint? AcceptedAuthenticationKey { get; init; }
    public Exception? Exception { get; init; }
    public int RedemptionCount { get; private set; }
    public uint? LastSessionUid { get; private set; }
    public uint? LastAuthenticationKey { get; private set; }

    public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedemptionCount++;
        LastSessionUid = sessionUid;
        LastAuthenticationKey = authenticationKey;

        if (Exception is not null)
        {
            throw Exception;
        }

        GameLoginTicketIdentity? result = AcceptedAuthenticationKey is uint expected && authenticationKey != expected ? null : Result;
        return ValueTask.FromResult(result);
    }
}

internal sealed class FakeAttemptLimiter : IGameLoginTicketRedemptionAttemptLimiter
{
    public int BeginCount { get; private set; }
    public IPAddress? LastRemoteAddress { get; private set; }
    public uint LastSessionUid { get; private set; }

    public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attemptLease)
    {
        BeginCount++;
        LastRemoteAddress = remoteAddress;
        LastSessionUid = sessionUid;
        attemptLease = new FakeAttemptLease();
        return true;
    }
}

internal sealed class FakeAttemptLease : IGameLoginTicketRedemptionAttemptLease
{
    public void Complete(bool authorizationAccepted)
    {
    }

    public void Dispose()
    {
    }
}
