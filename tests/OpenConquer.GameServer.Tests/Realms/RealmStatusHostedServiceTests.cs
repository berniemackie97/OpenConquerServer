using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Realms;

namespace OpenConquer.GameServer.Tests.Realms;

public sealed class RealmStatusHostedServiceTests
{
    private static readonly RealmStatusOptions s_options = new(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task StartAsync_PublishesTheCurrentLoadWithTheReportLifetime()
    {
        RecordingPublisher publisher = new();
        GameServerFailureState failureState = new();
        RealmStatusHostedService service = CreateService(publisher, new RealmLoadSample(RealmLoad.Busy, 37), failureState);

        await service.StartAsync(TestContext.Current.CancellationToken);
        Report report = await publisher.FirstReport.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Report(RealmLoad.Busy, 37, TimeSpan.FromSeconds(30)), report);
        Assert.Null(failureState.Failure);
    }

    [Fact]
    public async Task StopAsync_WithdrawsTheReport()
    {
        RecordingPublisher publisher = new();
        RealmStatusHostedService service = CreateService(publisher, new RealmLoadSample(RealmLoad.Normal, 0), new GameServerFailureState());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await publisher.FirstReport.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, publisher.WithdrawCount);
    }

    [Fact]
    public async Task DatabaseFailuresAreRetriedRatherThanStoppingTheServer()
    {
        RecordingPublisher publisher = new() { PublishFailure = new TestDatabaseException(), WithdrawFailure = new TestDatabaseException() };
        GameServerFailureState failureState = new();
        RealmStatusHostedService service = CreateService(publisher, new RealmLoadSample(RealmLoad.Normal, 0), failureState);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await publisher.FirstReport.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, publisher.WithdrawCount);
        Assert.Null(failureState.Failure);
    }

    [Fact]
    public void Options_RejectReportIntervalsOutsideTheSupportedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealmStatusOptions(TimeSpan.FromMilliseconds(999)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealmStatusOptions(TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1)));
    }

    private static RealmStatusHostedService CreateService(RecordingPublisher publisher, RealmLoadSample sample, GameServerFailureState failureState)
    {
        return new RealmStatusHostedService(publisher, () => new FixedLoadSource(sample), s_options, TimeProvider.System, failureState, NullLogger<RealmStatusHostedService>.Instance);
    }

    private readonly record struct Report(RealmLoad Load, int OnlineCount, TimeSpan Lifetime);

    private sealed class FixedLoadSource(RealmLoadSample sample) : IRealmLoadSource
    {
        public RealmLoadSample Sample() => sample;
    }

    private sealed class RecordingPublisher : IRealmStatusPublisher
    {
        private readonly TaskCompletionSource<Report> _firstReport = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? PublishFailure { get; init; }
        public Exception? WithdrawFailure { get; init; }
        public Task<Report> FirstReport => _firstReport.Task;
        public int WithdrawCount { get; private set; }

        public ValueTask PublishAsync(RealmLoad load, int onlineCount, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            _firstReport.TrySetResult(new Report(load, onlineCount, lifetime));
            return PublishFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(PublishFailure);
        }

        public ValueTask WithdrawAsync(CancellationToken cancellationToken = default)
        {
            WithdrawCount++;
            return WithdrawFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(WithdrawFailure);
        }
    }

    private sealed class TestDatabaseException() : DbException("The test database is unavailable.");
}
