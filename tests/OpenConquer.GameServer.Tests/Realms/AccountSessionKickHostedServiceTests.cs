using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.World.Presence;

namespace OpenConquer.GameServer.Tests.Realms;

public sealed class AccountSessionKickHostedServiceTests
{
    private const uint AccountId = 42;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint SessionUid = 0x1020_3040;

    [Fact]
    public async Task ProcessKicksAsync_DisconnectsTheKickedCharacter()
    {
        RecordingAccountSessionStore store = new();
        CharacterPresenceDirectory presence = new();
        using ICharacterPresenceLease registration = presence.Register(CharacterId);
        store.QueueKicks(new AccountSessionKick(AccountId, CharacterId, SessionUid));

        await CreateService(store, presence).ProcessKicksAsync(TestContext.Current.CancellationToken);

        Assert.True(registration.IsRevoked);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Empty(store.Left);
    }

    [Fact]
    public async Task ProcessKicksAsync_RemovesTheRecordOfAnAccountThatIsNotConnected()
    {
        RecordingAccountSessionStore store = new();
        store.QueueKicks(
            new AccountSessionKick(AccountId, CharacterId, SessionUid),
            new AccountSessionKick(AccountId + 1, CharacterId: null, SessionUid: null));

        await CreateService(store, new CharacterPresenceDirectory()).ProcessKicksAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(AccountId, SessionUid)], store.Left);
    }

    [Fact]
    public async Task ProcessKicksAsync_DatabaseFailuresAreLeftForTheNextPoll()
    {
        RecordingAccountSessionStore unreadable = new() { TakeKicksFailure = new TestDatabaseException() };
        RecordingAccountSessionStore unwritable = new() { LeaveFailure = new TestDatabaseException() };
        unwritable.QueueKicks(new AccountSessionKick(AccountId, CharacterId, SessionUid));

        await CreateService(unreadable, new CharacterPresenceDirectory()).ProcessKicksAsync(TestContext.Current.CancellationToken);
        await CreateService(unwritable, new CharacterPresenceDirectory()).ProcessKicksAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(AccountId, SessionUid)], unwritable.Left);
    }

    [Fact]
    public async Task StartAndStop_ClearThisRealmsSessionRecords()
    {
        RecordingAccountSessionStore store = new();
        GameServerFailureState failureState = new();
        AccountSessionKickHostedService service = CreateService(store, new CharacterPresenceDirectory(), failureState);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.ResetCount);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, store.ResetCount);
        Assert.Null(failureState.Failure);
    }

    [Fact]
    public async Task ResetFailure_FailsStartupButNotShutdown()
    {
        RecordingAccountSessionStore store = new() { ResetFailure = new TestDatabaseException() };
        AccountSessionKickHostedService service = CreateService(store, new CharacterPresenceDirectory());

        await Assert.ThrowsAsync<TestDatabaseException>(() => service.StartAsync(TestContext.Current.CancellationToken));
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, store.ResetCount);
    }

    [Fact]
    public async Task ExecuteAsync_PollsUntilStopped()
    {
        RecordingAccountSessionStore store = new();
        CharacterPresenceDirectory presence = new();
        using ICharacterPresenceLease registration = presence.Register(CharacterId);
        store.QueueKicks(new AccountSessionKick(AccountId, CharacterId, SessionUid));
        AccountSessionKickHostedService service = CreateService(store, presence);
        TaskCompletionSource revoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration revocationSignal = registration.RevocationToken.Register(() => revoked.TrySetResult());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await revoked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(registration.IsRevoked);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static AccountSessionKickHostedService CreateService(IAccountSessionStore store, ICharacterPresenceRevoker presence,
        GameServerFailureState? failureState = null)
    {
        return new AccountSessionKickHostedService(store, presence, TimeProvider.System, failureState ?? new GameServerFailureState(),
            NullLogger<AccountSessionKickHostedService>.Instance);
    }

    private sealed class TestDatabaseException() : DbException("The test database is unavailable.");
}
