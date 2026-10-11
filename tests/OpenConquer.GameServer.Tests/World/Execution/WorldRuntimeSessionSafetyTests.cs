using System.Threading.Channels;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldRuntimeSessionSafetyTests
{
    private const uint FirstCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint FirstMapId = 1002;
    private const uint SecondMapId = 1011;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void WorldExecutionOptions_InvalidCharacterLimits_AreRejected(int global, int perMap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldExecutionOptions(1, 1, 1, global, perMap));
    }

    [Fact]
    public async Task AdmitCharacterAsync_GlobalCapacityIncludesPendingAndActiveReservations()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 1, maxCharactersPerMap: 2);
        runtime.Start();

        WorldCharacterOwnership first = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId + 1, SecondMapId), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("capacity is exhausted", error.Message, StringComparison.Ordinal);
        Assert.True(await runtime.RemoveCharacterAsync(first, TestContext.Current.CancellationToken));
        WorldCharacterOwnership replacement = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId + 1, SecondMapId), TestContext.Current.CancellationToken);
        Assert.NotEqual(first.Generation, replacement.Generation);
    }

    [Fact]
    public async Task AdmitCharacterAsync_PerMapCapacityDoesNotBlockDifferentMap()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 3, maxCharactersPerMap: 1);
        runtime.Start();

        WorldCharacterOwnership first = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId + 1, FirstMapId), TestContext.Current.CancellationToken).AsTask());

        WorldCharacterOwnership second = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId + 1, SecondMapId), TestContext.Current.CancellationToken);

        Assert.NotNull(await runtime.ReadCharacterAsync(first, TestContext.Current.CancellationToken));
        Assert.NotNull(await runtime.ReadCharacterAsync(second, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ownership_StaleTokenFromPreviousRuntimeCannotAffectReplacement()
    {
        WorldCharacterOwnership stale;

        await using (WorldRuntime older = CreateRuntime(maxCharacters: 1, maxCharactersPerMap: 1))
        {
            older.Start();
            stale = await older.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);
        }

        await using WorldRuntime newer = CreateRuntime(maxCharacters: 1, maxCharactersPerMap: 1);
        newer.Start();

        WorldCharacterOwnership current = await newer.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);

        Assert.Equal(stale.Generation, current.Generation);
        Assert.NotEqual(stale.RuntimeId, current.RuntimeId);
        Assert.NotEqual(Guid.Empty, current.RuntimeId);
        Assert.False(await newer.RemoveCharacterAsync(stale, TestContext.Current.CancellationToken));
        Assert.Null(await newer.ReadCharacterAsync(stale, TestContext.Current.CancellationToken));
        Assert.NotNull(await newer.ReadCharacterAsync(current, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WorldPartition_ReservedRemovalLaneExecutesWhenOrdinaryQueueIsFull()
    {
        Channel<WorldPartition> ready = Channel.CreateBounded<WorldPartition>(1);
        WorldPartition partition = new(WorldMapTestContent.Create(FirstMapId, 1015, 0), ready.Writer, capacity: 1, maxCommandsPerTurn: 1, removalCapacity: 1);
        List<string> execution = [];
        WorldPartitionCommand<int> ordinary = new(_ => { execution.Add("ordinary"); return 1; });
        WorldPartitionCommand<int> rejected = new(_ => 3);
        WorldPartitionCommand<int> removal = new(_ => { execution.Add("removal"); return 2; });

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueue(ordinary));
        Assert.Equal(WorldPartitionEnqueueResult.CapacityExhausted, partition.TryEnqueue(rejected));
        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueueRemoval(removal));

        Assert.True(ready.Reader.TryRead(out WorldPartition? scheduled));
        Assert.Same(partition, scheduled);
        partition.ExecuteTurn();

        Assert.Equal(2, await removal.Completion);
        Assert.False(ordinary.Completion.IsCompleted);
        Assert.True(ready.Reader.TryRead(out scheduled));
        partition.ExecuteTurn();

        Assert.Equal(1, await ordinary.Completion);
        Assert.Equal(["removal", "ordinary"], execution);
        Assert.False(rejected.Completion.IsCompleted);
    }

    [Fact]
    public async Task ReservedLane_RejectsExcessRemovalWithoutDroppingExistingCleanup()
    {
        Channel<WorldPartition> ready = Channel.CreateBounded<WorldPartition>(1);
        WorldPartition partition = new(WorldMapTestContent.Create(FirstMapId, 1015, 0), ready.Writer, 1, 1, removalCapacity: 1);
        WorldPartitionCommand<int> removal = new(_ => 1);

        Assert.Equal(WorldPartitionEnqueueResult.Accepted, partition.TryEnqueueRemoval(removal));
        Assert.Throws<InvalidOperationException>(() => partition.TryEnqueueRemoval(new WorldPartitionCommand<int>(_ => 2)));
        Assert.True(ready.Reader.TryRead(out _));
        partition.ExecuteTurn();

        Assert.Equal(1, await removal.Completion);
    }

    [Fact]
    public async Task StopAsync_WaitsForTrackedSessionAndAdmissionCompletion()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 2);
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);
        TaskCompletionSource session = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource admission = new(TaskCreationOptions.RunContinuationsAsynchronously);

        runtime.TrackSession(ownership, session.Task);
        runtime.TrackAdmission(admission.Task);

        Task stopping = runtime.StopAsync();

        try
        {
            Assert.False(stopping.IsCompleted);

            session.TrySetResult();

            Assert.False(stopping.IsCompleted);

            admission.TrySetResult();

            await stopping.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        }
        finally
        {
            session.TrySetResult();
            admission.TrySetResult();
            runtime.UntrackSession(ownership, session.Task);
            runtime.UntrackAdmission(admission.Task);
        }

        Assert.False(runtime.IsRunning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(
            () => runtime.TrackAdmission(Task.CompletedTask), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReleaseCharacterAsync_AfterRuntimeShutdownIsSafeAndIdempotent()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 1, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(FirstCharacterId, FirstMapId), TestContext.Current.CancellationToken);

        await runtime.StopAsync();
        await runtime.ReleaseCharacterAsync(ownership);
        await runtime.ReleaseCharacterAsync(ownership);
    }

    private static WorldRuntime CreateRuntime(int maxCharacters, int maxCharactersPerMap)
    {
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(
            WorldMapTestContent.Create(FirstMapId, 1015, 0), WorldMapTestContent.Create(SecondMapId, 1016, 0));

        return new WorldRuntime(maps, new WorldExecutionOptions(1, 4, 1, maxCharacters, maxCharactersPerMap));
    }

    private static CharacterLoginProfile CreateProfile(uint characterId, uint mapId)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId, 0, 0);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, 0, 0, 0, location);
    }
}
