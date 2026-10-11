using System.Threading.Channels;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldPartitionTests
{
    [Fact]
    public async Task TryEnqueue_RejectsWorkBeyondBoundedCapacity()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer, capacity: 2);
        WorldPartitionCommand<int> first = new(_ => 1);
        WorldPartitionCommand<int> second = new(_ => 2);
        WorldPartitionCommand<int> rejected = new(_ => 3);

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(first));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(second));
        Assert.Equal(WorldPartitionEnqueueResult.CapacityExhausted, partition.TryEnqueue(rejected));

        Assert.True(ready.Reader.TryRead(out WorldPartition? scheduled));
        Assert.Same(partition, scheduled);
        Assert.False(ready.Reader.TryRead(out _));

        partition.ExecuteTurn();

        Assert.Equal(1, await first.Completion);
        Assert.Equal(2, await second.Completion);
        Assert.False(rejected.Completion.IsCompleted);
    }

    [Fact]
    public void ExecuteTurn_ProcessesCommandsInAcceptanceOrder()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);
        List<int> executionOrder = [];

        WorldPartitionCommand<int>[] commands = Enumerable.Range(1, 4)
            .Select(index => new WorldPartitionCommand<int>(_ =>
            {
                executionOrder.Add(index);
                return index;
            }))
            .ToArray();

        foreach (WorldPartitionCommand<int> command in commands)
        {
            Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(command));
        }

        Assert.True(ready.Reader.TryRead(out _));
        partition.ExecuteTurn();

        Assert.Equal([1, 2, 3, 4], executionOrder);
        Assert.All(commands, command => Assert.True(command.Completion.IsCompletedSuccessfully));
    }

    [Fact]
    public void ExecuteTurn_ReschedulesAfterQuantumAndPreservesFairness()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition first = CreatePartition(ready.Writer, mapId: 1002, capacity: 8, maxCommandsPerTurn: 1);
        WorldPartition second = CreatePartition(ready.Writer, mapId: 1011, capacity: 8, maxCommandsPerTurn: 1);
        List<string> order = [];

        for (int index = 0; index < 3; index++)
        {
            Assert.Equal(WorldPartitionEnqueueResult.Accepted, first.TryEnqueue(
                new WorldPartitionCommand<int>(_ =>
                {
                    order.Add("first");
                    return 1;
                })));
        }

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, second.TryEnqueue(
            new WorldPartitionCommand<int>(_ =>
            {
                order.Add("second");
                return 1;
            })));

        Assert.True(ready.Reader.TryRead(out WorldPartition? initial));
        Assert.Same(first, initial);
        first.ExecuteTurn();

        Assert.True(ready.Reader.TryRead(out WorldPartition? next));
        Assert.Same(second, next);
        second.ExecuteTurn();

        Assert.True(ready.Reader.TryRead(out WorldPartition? remaining));
        Assert.Same(first, remaining);
        first.ExecuteTurn();

        Assert.True(ready.Reader.TryRead(out remaining));
        Assert.Same(first, remaining);
        first.ExecuteTurn();

        Assert.False(ready.Reader.TryRead(out _));
        Assert.Equal(["first", "second", "first", "first"], order);
    }

    [Fact]
    public async Task ExecuteTurn_OverlappingInvocationCannotMutateSamePartition()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);
        using ManualResetEventSlim started = new(false);
        using ManualResetEventSlim release = new(false);

        WorldPartitionCommand<int> command = new(_ =>
        {
            started.Set();

            if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            {
                throw new TimeoutException("The test did not release the executing partition.");
            }

            return 1;
        });

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(command));
        Assert.True(ready.Reader.TryRead(out _));

        Task execution = Task.Run(partition.ExecuteTurn, TestContext.Current.CancellationToken);

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(partition.ExecuteTurn);

            Assert.Contains("already executing", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            release.Set();
        }

        await execution;

        Assert.Equal(1, await command.Completion);
    }

    [Fact]
    public async Task ExecuteTurn_FailedCommandDoesNotSkipSubsequentCommands()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);
        IOException failure = new("test command failed");
        WorldPartitionCommand<int> first = new(_ => throw failure);
        WorldPartitionCommand<int> second = new(_ => 42);

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(first));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(second));
        Assert.True(ready.Reader.TryRead(out _));

        partition.ExecuteTurn();

        IOException exception = await Assert.ThrowsAsync<IOException>(() => first.Completion);

        Assert.Same(failure, exception);
        Assert.Equal(42, await second.Completion);
    }

    [Fact]
    public async Task Stop_RejectsEveryPendingCommandExactlyOnce()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);
        int callbacks = 0;
        WorldPartitionCommand<int> first = new(_ => 1, _ => Interlocked.Increment(ref callbacks));
        WorldPartitionCommand<int> second = new(_ => 2, _ => Interlocked.Increment(ref callbacks));

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(first));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(second));

        partition.Stop();
        partition.Stop();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Completion);

        Assert.Equal(2, callbacks);
        Assert.Equal(WorldPartitionEnqueueResult.Stopped, partition.TryEnqueue(new WorldPartitionCommand<int>(_ => 3)));

        partition.Clear();
    }

    [Fact]
    public void TryEnqueue_CompletedSchedulerQueueDoesNotAcceptOrStrandWork()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);
        WorldPartitionCommand<int> command = new(_ => 1);

        ready.Writer.TryComplete();

        Assert.Equal(WorldPartitionEnqueueResult.Stopped, partition.TryEnqueue(command));
        Assert.False(command.Completion.IsCompleted);
    }

    [Fact]
    public void Clear_RequiresStoppedPartition()
    {
        Channel<WorldPartition> ready = CreateReadyChannel();
        WorldPartition partition = CreatePartition(ready.Writer);

        Assert.Throws<InvalidOperationException>(partition.Clear);

        partition.Stop();
        partition.Clear();
    }

    private static Channel<WorldPartition> CreateReadyChannel() => Channel.CreateBounded<WorldPartition>(new BoundedChannelOptions(2)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    private static WorldPartition CreatePartition(ChannelWriter<WorldPartition> ready, uint mapId = 1002, int capacity = 8, int maxCommandsPerTurn = 8)
    {
        return new WorldPartition(WorldMapTestContent.Create(mapId, mapId + 13, 0), ready, capacity, maxCommandsPerTurn);
    }
}
