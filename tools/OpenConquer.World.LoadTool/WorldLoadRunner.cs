using System.Runtime;
using System.Runtime.InteropServices;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.World.LoadTool;

internal sealed class WorldLoadRunner(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<WorldLoadReport> RunAsync(WorldLoadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(options.TimeoutSeconds), _timeProvider);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        CancellationToken operation = deadline.Token;
        WorldExecutionOptions execution = new(options.Workers, options.QueueCapacity, options.TurnBudget,
            options.Sessions, options.Sessions);
        await using WorldRuntime runtime = new(WorldLoadFixture.CreateMaps(options.Maps), execution);
        runtime.Start();
        WorldCharacterOwnership[] owners = new WorldCharacterOwnership[options.Sessions];
        for (int index = 0; index < owners.Length; index++)
        {
            owners[index] = await runtime.AdmitCharacterAsync(WorldLoadFixture.CreateCharacter(index, options.Maps), operation).ConfigureAwait(false);
        }

        await MeasureAsync(runtime, owners, options.Warmup, 0, operation).ConfigureAwait(false);
        WorldLoadMeasurement measurement = await MeasureAsync(runtime, owners, options.Steps, options.Warmup, operation).ConfigureAwait(false);

        ushort finalY = (ushort)(1 + ((options.Warmup + options.Steps) & 1));
        foreach (WorldCharacterOwnership owner in owners)
        {
            WorldCharacterSnapshot? snapshot = await runtime.ReadCharacterAsync(owner, operation).ConfigureAwait(false);
            RequireSnapshot(snapshot, owner, finalY);
        }

        if (runtime.ResidentCount != owners.Length)
        {
            throw new InvalidOperationException("The measured run lost character residency.");
        }

        ThreadPool.GetMinThreads(out int minimumWorkers, out _);
        return new WorldLoadReport(1, _timeProvider.GetUtcNow(), options,
            new WorldLoadEnvironment(RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), System.Environment.ProcessorCount, minimumWorkers, GCSettings.IsServerGC),
            measurement, owners.Length, finalY);
    }

    private async Task<WorldLoadMeasurement> MeasureAsync(WorldRuntime runtime, WorldCharacterOwnership[] owners,
        int steps, int previousSteps, CancellationToken cancellationToken)
    {
        long[] completion = new long[checked(owners.Length * steps)];
        long[] submission = new long[completion.Length];
        long[] allocations = new long[owners.Length];
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] clients = new Task[owners.Length];
        for (int index = 0; index < owners.Length; index++)
        {
            clients[index] = WalkAsync(index);
        }
        Task allClients = Task.WhenAll(clients);

        // Setup/sample storage and client state machines are outside the measured window.
        // Retained resident state survives this collection, matching an already admitted world.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long began = _timeProvider.GetTimestamp();
        start.SetResult();
        await allClients.ConfigureAwait(false);
        double seconds = _timeProvider.GetElapsedTime(began).TotalSeconds;
        long processBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        int gen0Collections = GC.CollectionCount(0) - gen0;
        int gen1Collections = GC.CollectionCount(1) - gen1;
        int gen2Collections = GC.CollectionCount(2) - gen2;
        long submissionBytes = allocations.Sum();
        return new WorldLoadMeasurement(completion.Length, seconds, completion.Length / seconds,
            LatencyPercentiles.FromTicks(completion, _timeProvider.TimestampFrequency),
            LatencyPercentiles.FromTicks(submission, _timeProvider.TimestampFrequency),
            submissionBytes, (double)submissionBytes / completion.Length, processBytes, (double)processBytes / completion.Length,
            gen0Collections, gen1Collections, gen2Collections);

        async Task WalkAsync(int index)
        {
            await start.Task.ConfigureAwait(false);
            WorldCharacterOwnership owner = owners[index];
            int offset = index * steps;
            long allocated = 0;
            for (int step = 0; step < steps; step++)
            {
                // Existing 5517 ground directions 0/4 are opposites; see the direction-table note
                // (0x86C608 / 0x86C628). This bounded loop changes no movement semantics.
                byte direction = ((previousSteps + step) & 1) == 0 ? (byte)0 : (byte)4;
                long before = GC.GetAllocatedBytesForCurrentThread();
                long timestamp = _timeProvider.GetTimestamp();
                ValueTask<WorldCharacterSnapshot?> pending = runtime.TryWalkCharacterAsync(owner, direction, cancellationToken);
                long submitted = _timeProvider.GetTimestamp();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                // No await occurs between the thread-local allocation reads.
                submission[offset + step] = submitted - timestamp;
                WorldCharacterSnapshot? snapshot = await pending.ConfigureAwait(false);
                completion[offset + step] = _timeProvider.GetTimestamp() - timestamp;
                RequireSnapshot(snapshot, owner, direction == 0 ? (ushort)2 : (ushort)1);
            }
            allocations[index] = allocated;
        }
    }

    private static void RequireSnapshot(WorldCharacterSnapshot? snapshot, WorldCharacterOwnership owner, ushort y)
    {
        if (snapshot is not { } position || position.Ownership != owner || position.X != 1 || position.Y != y)
        {
            // A rejected/faulted/misdirected command invalidates the entire trial, not just a sample.
            throw new InvalidOperationException("A measured walk did not commit its expected position and ownership.");
        }
    }
}
