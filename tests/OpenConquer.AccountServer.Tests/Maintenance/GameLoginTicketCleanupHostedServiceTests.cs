using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.AccountServer.Hosting;
using OpenConquer.AccountServer.Maintenance;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

namespace OpenConquer.AccountServer.Tests.Maintenance;

public sealed class GameLoginTicketCleanupHostedServiceTests
{
    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        TrackingExpirationCleaner cleaner = new(maximumBatchSize: 1_000);
        GameLoginTicketCleanupConfiguration configuration = new(TimeSpan.FromMinutes(1), maximumBatchesPerRun: 10);
        FatalBackgroundServiceFailureState fatalFailureState = new();

        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketCleanupHostedService(null!, configuration, TimeProvider.System, fatalFailureState, NullLogger<GameLoginTicketCleanupHostedService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketCleanupHostedService(cleaner, null!, TimeProvider.System, fatalFailureState, NullLogger<GameLoginTicketCleanupHostedService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketCleanupHostedService(cleaner, configuration, null!, fatalFailureState, NullLogger<GameLoginTicketCleanupHostedService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketCleanupHostedService(cleaner, configuration, TimeProvider.System, null!, NullLogger<GameLoginTicketCleanupHostedService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketCleanupHostedService(cleaner, configuration, TimeProvider.System, fatalFailureState, null!));
    }

    [Fact]
    public async Task DeleteExpiredTicketsAsync_StopsAfterPartialBatch()
    {
        TrackingExpirationCleaner cleaner = new(maximumBatchSize: 1_000, 1_000, 250);
        GameLoginTicketCleanupHostedService service = CreateService(cleaner, maximumBatchesPerRun: 10);

        int deleted = await service.DeleteExpiredTicketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1_250, deleted);
        Assert.Equal(2, cleaner.CallCount);
    }

    [Fact]
    public async Task DeleteExpiredTicketsAsync_StopsAtConfiguredBatchLimit()
    {
        TrackingExpirationCleaner cleaner = new(maximumBatchSize: 1_000, 1_000, 1_000, 1_000);
        GameLoginTicketCleanupHostedService service = CreateService(cleaner, maximumBatchesPerRun: 2);

        int deleted = await service.DeleteExpiredTicketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2_000, deleted);
        Assert.Equal(2, cleaner.CallCount);
    }

    [Fact]
    public async Task DeleteExpiredTicketsAsync_PropagatesCancellation()
    {
        TrackingExpirationCleaner cleaner = new(maximumBatchSize: 1_000);
        GameLoginTicketCleanupHostedService service = CreateService(cleaner, maximumBatchesPerRun: 1);
        using CancellationTokenSource cancellation = new();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.DeleteExpiredTicketsAsync(cancellation.Token));
        Assert.Equal(0, cleaner.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CleanupFailureDoesNotTerminateServiceOrRecordFatalFailure()
    {
        FailingExpirationCleaner cleaner = new();
        using GameLoginTicketCleanupHostedService service = CreateService(cleaner, maximumBatchesPerRun: 1, out FatalBackgroundServiceFailureState fatalFailureState);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await cleaner.Called.WaitAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask!.IsCompleted);
        Assert.Null(fatalFailureState.Failure);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        Assert.Null(fatalFailureState.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_ContractViolationRecordsFatalFailureAndTerminatesService()
    {
        ContractViolatingExpirationCleaner cleaner = new();
        using GameLoginTicketCleanupHostedService service = CreateService(cleaner, maximumBatchesPerRun: 1, out FatalBackgroundServiceFailureState fatalFailureState);

        await service.StartAsync(TestContext.Current.CancellationToken);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTask!);

        Assert.Contains("reported", exception.Message, StringComparison.Ordinal);
        Assert.Same(exception, fatalFailureState.Failure);
    }

    private static GameLoginTicketCleanupHostedService CreateService(IGameLoginTicketExpirationCleaner cleaner, int maximumBatchesPerRun)
    {
        return CreateService(cleaner, maximumBatchesPerRun, out _);
    }

    private static GameLoginTicketCleanupHostedService CreateService(IGameLoginTicketExpirationCleaner cleaner, int maximumBatchesPerRun, out FatalBackgroundServiceFailureState fatalFailureState)
    {
        fatalFailureState = new FatalBackgroundServiceFailureState();

        return new GameLoginTicketCleanupHostedService(
            cleaner,
            new GameLoginTicketCleanupConfiguration(TimeSpan.FromDays(1), maximumBatchesPerRun),
            TimeProvider.System,
            fatalFailureState,
            NullLogger<GameLoginTicketCleanupHostedService>.Instance);
    }

    private sealed class ContractViolatingExpirationCleaner : IGameLoginTicketExpirationCleaner
    {
        public int MaximumBatchSize => 1_000;

        public ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(1_001);
        }
    }

    private sealed class TrackingExpirationCleaner(int maximumBatchSize, params int[] results) : IGameLoginTicketExpirationCleaner
    {
        private readonly Queue<int> _results = new(results);

        public int MaximumBatchSize { get; } = maximumBatchSize;
        public int CallCount { get; private set; }

        public ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            return ValueTask.FromResult(_results.Count == 0 ? 0 : _results.Dequeue());
        }
    }

    private sealed class FailingExpirationCleaner : IGameLoginTicketExpirationCleaner
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumBatchSize => 1_000;
        public Task Called => _called.Task;

        public ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
        {
            _called.TrySetResult();
            return ValueTask.FromException<int>(new IOException("cleanup failed"));
        }
    }
}
