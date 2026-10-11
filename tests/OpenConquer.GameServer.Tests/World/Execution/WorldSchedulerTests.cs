using OpenConquer.Application.World;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldSchedulerTests
{
    [Fact]
    public async Task Scheduler_IndependentPartitionsExecuteConcurrently()
    {
        await using WorldScheduler scheduler = CreateScheduler(workerCount: 2, maxCommandsPerTurn: 1);

        Assert.True(scheduler.TryGetPartition(1002, out WorldPartition? first));
        Assert.True(scheduler.TryGetPartition(1011, out WorldPartition? second));

        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new(false);

        WorldPartitionCommand<int> firstCommand = new(_ =>
        {
            firstStarted.TrySetResult();

            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The first partition was not released.");
            }

            return 1;
        });

        WorldPartitionCommand<int> secondCommand = new(_ =>
        {
            secondStarted.TrySetResult();

            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The second partition was not released.");
            }

            return 2;
        });

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, first.TryEnqueue(firstCommand));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, second.TryEnqueue(secondCommand));

        scheduler.Start();

        try
        {
            await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(firstCommand.Completion.IsCompleted);
            Assert.False(secondCommand.Completion.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(1, await firstCommand.Completion);
        Assert.Equal(2, await secondCommand.Completion);
        Assert.True(scheduler.IsRunning);
    }

    [Fact]
    public async Task Scheduler_FailedCommandDoesNotTerminateWorker()
    {
        await using WorldScheduler scheduler = CreateScheduler(workerCount: 1);

        Assert.True(scheduler.TryGetPartition(1002, out WorldPartition? partition));

        IOException failure = new("command failure");
        WorldPartitionCommand<int> first = new(_ => throw failure);
        WorldPartitionCommand<int> second = new(_ => 42);

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(first));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(second));

        scheduler.Start();

        IOException exception = await Assert.ThrowsAsync<IOException>(() => first.Completion);

        Assert.Same(failure, exception);
        Assert.Equal(42, await second.Completion);
        Assert.True(scheduler.IsRunning);

        WorldPartitionCommand<int> subsequent = new(_ => 84);

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(subsequent));
        Assert.Equal(84, await subsequent.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
        Assert.True(scheduler.IsRunning);
    }

    [Fact]
    public async Task Scheduler_UnexpectedWorkerFailureNotifiesOwnerAndStopsExecution()
    {
        TaskCompletionSource<Exception> notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WorldScheduler scheduler = CreateScheduler(workerCount: 1, onWorkerFailure: exception => notified.TrySetResult(exception));

        Assert.True(scheduler.TryGetPartition(1002, out WorldPartition? partition));

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new(false);

        WorldPartitionCommand<int> command = new(_ =>
        {
            started.TrySetResult();

            if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            {
                throw new TimeoutException("The executing partition was not released.");
            }

            return 1;
        });

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(command));

        Task executing = Task.Run(partition.ExecuteTurn, TestContext.Current.CancellationToken);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            scheduler.Start();

            Exception failure = await notified.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            InvalidOperationException exception = Assert.IsType<InvalidOperationException>(failure);

            Assert.Contains("already executing", exception.Message, StringComparison.Ordinal);
            Assert.False(scheduler.IsRunning);
        }
        finally
        {
            release.Set();
        }

        await executing;
        Assert.Equal(1, await command.Completion);

        InvalidOperationException shutdownFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.StopAsync().WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

        Assert.Contains("already executing", shutdownFailure.Message, StringComparison.Ordinal);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public async Task StopAsync_RejectsQueuedWorkButWaitsForExecutingTurn()
    {
        await using WorldScheduler scheduler = CreateScheduler(workerCount: 1, maxCommandsPerTurn: 1);

        Assert.True(scheduler.TryGetPartition(1002, out WorldPartition? partition));

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new(false);

        WorldPartitionCommand<int> executing = new(_ =>
        {
            started.TrySetResult();

            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The executing command was not released.");
            }

            return 1;
        });

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(executing));

        scheduler.Start();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        WorldPartitionCommand<int> pending = new(_ => 2);
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(pending));

        Task stopping = scheduler.StopAsync();

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending.Completion.WaitAsync(
                TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

            Assert.False(executing.Completion.IsCompleted);
            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(1, await executing.Completion);
        await stopping;

        Assert.False(scheduler.IsRunning);
        Assert.Equal(WorldPartitionEnqueueResult.Stopped, partition.TryEnqueue(new WorldPartitionCommand<int>(_ => 3)));
    }

    [Fact]
    public async Task StopAsync_IsIdempotentAndPreventsRestart()
    {
        await using WorldScheduler scheduler = CreateScheduler(workerCount: 1);

        scheduler.Start();

        Task first = scheduler.StopAsync();
        Task second = scheduler.StopAsync();

        Assert.Same(first, second);

        await first;

        Assert.False(scheduler.IsRunning);
        Assert.Throws<InvalidOperationException>(scheduler.Start);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task StopBeforeStart_RejectsSubsequentStartup()
    {
        await using WorldScheduler scheduler = CreateScheduler(workerCount: 1);

        await scheduler.StopAsync();

        Assert.Throws<InvalidOperationException>(scheduler.Start);
    }

    private static WorldScheduler CreateScheduler(int workerCount, int maxCommandsPerTurn = 8, Action<Exception>? onWorkerFailure = null)
    {
        WorldMapContent first = WorldMapTestContent.Create(1002, 1015, 0);
        WorldMapContent second = WorldMapTestContent.Create(1011, 1016, 0);
        WorldMapContentCatalog catalog = WorldMapTestContent.CreateCatalog(first, second);

        return new WorldScheduler(catalog, new WorldExecutionOptions(workerCount, partitionQueueCapacity: 16, maxCommandsPerTurn), onWorkerFailure);
    }
}
