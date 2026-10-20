using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Login.Bootstrap;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap;

public sealed class FencedCharacterAdmissionTests
{
    private const uint AccountId = 57;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private static readonly CharacterLocation s_original = new(1002, 431, 379);
    private static readonly CharacterLocation s_destination = new(1011, 193, 266);

    [Fact]
    public async Task Acquire_RehydratesFromReadOnlyRepositoryAfterRotatingGeneration()
    {
        CharacterLoginProfile earlier = Profile(s_original);
        CharacterLoginProfile current = Profile(s_destination);
        Guid token = Guid.NewGuid();
        PersistedCharacterLocation persisted = new(s_destination, revision: 17, lastOperationId: Guid.NewGuid(), writerToken: token);
        FakeFence fence = new() { Authority = new(AccountId, CharacterId, token, persisted) };
        FakeProfiles profiles = new() { Profile = current };
        FakeObserver observer = new() { Location = persisted };
        FencedCharacterAdmission admission = new(fence, observer, profiles);

        var (profile, authority) = await admission.AcquireAsync(earlier, TestContext.Current.CancellationToken);

        Assert.Same(current, profile);
        Assert.Same(fence.Authority, authority);
        Assert.Equal(1, fence.Calls);
        Assert.Equal(1, profiles.Calls);
        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task Acquire_NullFenceFailsWithoutProfileReadOrWorldAdmission()
    {
        FakeFence fence = new();
        FakeProfiles profiles = new() { Profile = Profile(s_original) };
        FakeObserver observer = new();
        FencedCharacterAdmission admission = new(fence, observer, profiles);

        await Assert.ThrowsAsync<InvalidDataException>(() => admission.AcquireAsync(Profile(s_original), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, fence.Calls);
        Assert.Equal(0, profiles.Calls);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task Acquire_RehydrationWithAnotherCharacterFailsClosed()
    {
        CharacterLoginProfile original = Profile(s_original);
        Guid token = Guid.NewGuid();
        PersistedCharacterLocation persisted = new(s_original, 0, writerToken: token);
        FakeFence fence = new() { Authority = new(AccountId, CharacterId, token, persisted) };
        FakeProfiles profiles = new() { Profile = Profile(s_original, characterId: CharacterId + 1) };
        FakeObserver observer = new() { Location = persisted };
        FencedCharacterAdmission admission = new(fence, observer, profiles);

        await Assert.ThrowsAsync<InvalidDataException>(() => admission.AcquireAsync(original, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task Acquire_StalePreFenceProfileIsNotAcceptedWhenRehydrationDisagreesWithFence()
    {
        Guid token = Guid.NewGuid();
        PersistedCharacterLocation persisted = new(s_destination, 8, writerToken: token);
        FakeFence fence = new() { Authority = new(AccountId, CharacterId, token, persisted) };
        FakeProfiles profiles = new() { Profile = Profile(s_original) };
        FakeObserver observer = new() { Location = persisted };
        FencedCharacterAdmission admission = new(fence, observer, profiles);

        await Assert.ThrowsAsync<InvalidDataException>(() => admission.AcquireAsync(Profile(s_original), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Verify_LaterWriterGenerationRevokesEarlierAdmission()
    {
        Guid initial = Guid.NewGuid();
        Guid replacement = Guid.NewGuid();
        PersistedCharacterLocation acquired = new(s_original, 12, writerToken: initial);
        CharacterLocationWriteAuthority authority = new(AccountId, CharacterId, initial, acquired);
        FakeObserver observer = new() { Location = new(s_original, 12, writerToken: replacement) };
        FencedCharacterAdmission admission = new(new FakeFence(), observer, new FakeProfiles());

        await Assert.ThrowsAsync<InvalidDataException>(() => admission.VerifyAsync(Profile(s_original), authority, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Verify_SameCoordinatesWithNewRevisionOrOperationMarkerFails()
    {
        Guid token = Guid.NewGuid();
        Guid operation = Guid.NewGuid();
        PersistedCharacterLocation acquired = new(s_original, 12, operation, token);
        CharacterLocationWriteAuthority authority = new(AccountId, CharacterId, token, acquired);
        FakeObserver observer = new() { Location = new(s_original, 13, operation, token) };
        FencedCharacterAdmission admission = new(new FakeFence(), observer, new FakeProfiles());

        await Assert.ThrowsAsync<InvalidDataException>(() => admission.VerifyAsync(Profile(s_original), authority, TestContext.Current.CancellationToken).AsTask());

        observer.Location = new PersistedCharacterLocation(s_original, 12, Guid.NewGuid(), token);
        await Assert.ThrowsAsync<InvalidDataException>(() => admission.VerifyAsync(Profile(s_original), authority, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Acquire_PreCancelledDoesNotDispatchRotation()
    {
        FakeFence fence = new();
        FencedCharacterAdmission admission = new(fence, new FakeObserver(), new FakeProfiles());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admission.AcquireAsync(Profile(s_original), cancellation.Token).AsTask());
        Assert.Equal(0, fence.Calls);
    }

    private static CharacterLoginProfile Profile(CharacterLocation location, uint characterId = CharacterId)
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(characterId, AccountId, "Test"),
            new CharacterAppearance(2011003, 339),
            new CharacterProgression(120, 123456789, 60, 10, 20, 0, 0),
            new CharacterAttributes(101, 102, 103, 104, 105),
            new CharacterVitals(1234, 567),
            new CharacterEconomy(1_234_567, 2_345, 678),
            -25, 321, 1234, location);
    }

    private sealed class FakeFence : ICharacterLocationFenceStore
    {
        public CharacterLocationWriteAuthority? Authority { get; init; }
        public int Calls { get; private set; }

        public ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountId, accountId);
            Assert.Equal(CharacterId, characterId);
            Calls++;
            return ValueTask.FromResult(Authority);
        }
    }

    private sealed class FakeObserver : ICharacterLocationObservationStore
    {
        public PersistedCharacterLocation? Location { get; set; }
        public int Calls { get; private set; }

        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountId, accountId);
            Assert.Equal(CharacterId, characterId);
            Calls++;
            return ValueTask.FromResult(Location);
        }
    }

    private sealed class FakeProfiles : ICharacterLoginProfileRepository
    {
        public CharacterLoginProfile? Profile { get; set; }
        public int Calls { get; private set; }

        public ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountId, accountId);
            Calls++;
            return ValueTask.FromResult(Profile);
        }
    }
}
