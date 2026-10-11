using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldRuntimeTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint OtherMapId = 1011;

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public void Options_InvalidCapacityOrExecutionLimits_AreRejected(int workers, int capacity, int quantum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldExecutionOptions(workers, capacity, quantum));
    }

    [Fact]
    public async Task AdmitCharacterAsync_CreatesAuthoritativeOwnershipAndSnapshot()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        CharacterLoginProfile profile = CreateProfile();
        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));

        Assert.Equal(CharacterId, ownership.CharacterId);
        Assert.Equal(MapId, ownership.MapId);
        Assert.NotEqual(0UL, ownership.Generation);
        Assert.Equal(ownership, snapshot.Ownership);
        Assert.Equal((ushort)0, snapshot.X);
        Assert.Equal((ushort)0, snapshot.Y);

        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmitCharacterAsync_DuplicateAcrossDifferentMaps_IsRejected()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);
        CharacterLoginProfile conflicting = CreateProfile(mapId: OtherMapId);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AdmitCharacterAsync(conflicting, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("already has a reserved or active world owner", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmitCharacterAsync_UnavailableMap_IsRejectedWithoutReservingCharacter()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => runtime.AdmitCharacterAsync(CreateProfile(mapId: 9999), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("unavailable world map", exception.Message, StringComparison.Ordinal);

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Assert.NotEqual(0UL, ownership.Generation);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(ushort.MaxValue, ushort.MaxValue)]
    public async Task AdmitCharacterAsync_OutOfBoundsPersistedPosition_IsRejectedWithoutReservingCharacter(ushort x, ushort y)
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => runtime.AdmitCharacterAsync(CreateProfile(x: x, y: y), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("out-of-bounds persisted position", exception.Message, StringComparison.Ordinal);

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Assert.NotEqual(0UL, ownership.Generation);
    }

    [Fact]
    public async Task RemoveCharacterAsync_StaleGenerationCannotRemoveReplacementOnAnotherMap()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership original = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Assert.True(await runtime.RemoveCharacterAsync(original, TestContext.Current.CancellationToken));

        WorldCharacterOwnership replacement = await runtime.AdmitCharacterAsync(CreateProfile(mapId: OtherMapId), TestContext.Current.CancellationToken);

        Assert.NotEqual(original.Generation, replacement.Generation);
        Assert.Equal(OtherMapId, replacement.MapId);
        Assert.False(await runtime.RemoveCharacterAsync(original, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.ReadCharacterAsync(original, TestContext.Current.CancellationToken));

        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(await runtime.ReadCharacterAsync(replacement, TestContext.Current.CancellationToken));

        Assert.Equal(replacement, snapshot.Ownership);
        Assert.True(await runtime.RemoveCharacterAsync(replacement, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveCharacterAsync_UnknownOrDefaultOwnership_DoesNotAffectActiveCharacter()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership current = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);
        WorldCharacterOwnership wrongGeneration = current with { Generation = current.Generation + 1 };
        WorldCharacterOwnership wrongMap = current with { MapId = OtherMapId };

        Assert.False(await runtime.RemoveCharacterAsync(default, TestContext.Current.CancellationToken));
        Assert.False(await runtime.RemoveCharacterAsync(wrongGeneration, TestContext.Current.CancellationToken));
        Assert.False(await runtime.RemoveCharacterAsync(wrongMap, TestContext.Current.CancellationToken));
        Assert.NotNull(await runtime.ReadCharacterAsync(current, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmitCharacterAsync_PreCanceledOperationDoesNotReserveOwnership()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.AdmitCharacterAsync(CreateProfile(), cancellation.Token).AsTask());

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Assert.NotEqual(0UL, ownership.Generation);
    }

    [Fact]
    public async Task AdmitCharacterAsync_CancellationAfterAcceptanceDoesNotAbandonReservation()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        ValueTask<WorldCharacterOwnership> accepted = runtime.AdmitCharacterAsync(CreateProfile(), cancellation.Token);

        cancellation.Cancel();

        WorldCharacterOwnership ownership = await accepted;

        Assert.NotNull(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmitCharacterAsync_ConcurrentRequestsReserveExactlyOneOwner()
    {
        await using WorldRuntime runtime = CreateRuntime(queueCapacity: 128);
        runtime.Start();

        CharacterLoginProfile profile = CreateProfile();

        Task<WorldCharacterOwnership?>[] attempts = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (WorldCharacterOwnership?)await runtime.AdmitCharacterAsync(profile);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        WorldCharacterOwnership?[] results = await Task.WhenAll(attempts);
        WorldCharacterOwnership ownership = Assert.Single(results, static result => result.HasValue)!.Value;

        Assert.NotNull(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveCharacterAsync_ConcurrentRequestsReleaseExactlyOneOwner()
    {
        await using WorldRuntime runtime = CreateRuntime(queueCapacity: 128);
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Task<bool>[] attempts = Enumerable.Range(0, 64).Select(_ => Task.Run(
            async () => await runtime.RemoveCharacterAsync(ownership),
            TestContext.Current.CancellationToken)).ToArray();

        bool[] results = await Task.WhenAll(attempts);

        Assert.Single(results, static result => result);
        Assert.Null(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));

        WorldCharacterOwnership replacement = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Assert.True(replacement.Generation > ownership.Generation);
    }

    [Fact]
    public async Task ReadCharacterAsync_PreCanceledOperationDoesNotReadOrChangeOwnership()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.ReadCharacterAsync(ownership, cancellation.Token).AsTask());

        Assert.NotNull(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveCharacterAsync_PreCanceledOperationDoesNotRemoveCharacter()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.RemoveCharacterAsync(ownership, cancellation.Token).AsTask());

        Assert.NotNull(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StopAsync_RejectsNewOperationsAndCannotRestart()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        Task first = runtime.StopAsync();
        Task second = runtime.StopAsync();

        Assert.Same(first, second);

        await first;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken).AsTask());

        Assert.Throws<InvalidOperationException>(runtime.Start);

        await runtime.StopAsync();
    }

    [Fact]
    public async Task StopBeforeStart_PreventsSubsequentStartupAndAdmission()
    {
        await using WorldRuntime runtime = CreateRuntime();

        await runtime.StopAsync();

        Assert.Throws<InvalidOperationException>(runtime.Start);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken).AsTask());
    }

    private static WorldRuntime CreateRuntime(int workerCount = 2, int queueCapacity = 32, int maxCommandsPerTurn = 8)
    {
        WorldMapContent first = WorldMapTestContent.Create(MapId, 1015, 0);
        WorldMapContent second = WorldMapTestContent.Create(OtherMapId, 1016, 0);
        WorldMapContentCatalog catalog = WorldMapTestContent.CreateCatalog(first, second);

        return new WorldRuntime(catalog, new WorldExecutionOptions(workerCount, queueCapacity, maxCommandsPerTurn));
    }

    private static CharacterLoginProfile CreateProfile(uint mapId = MapId, ushort x = 0, ushort y = 0)
    {
        CharacterLoginIdentity identity = new(CharacterId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId, x, y);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy,
            pkPoints: 0, titleId: 0, enlightenmentPoints: 0, location);
    }
}
