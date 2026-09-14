using OpenConquer.Application.Characters.Login;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters;

public sealed class CharacterLoginResolverTests
{
    [Fact]
    public void Constructor_NullRepositoryIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CharacterLoginResolver(null!));
    }

    [Fact]
    public async Task ResolveAsync_ZeroAccountIdIsRejected()
    {
        StubRepository repository = new();
        CharacterLoginResolver resolver = new(repository);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await resolver.ResolveAsync(0, TestContext.Current.CancellationToken));

        Assert.Equal(0, repository.FindCallCount);
    }

    [Fact]
    public async Task ResolveAsync_NoPersistedCharacter_RoutesToCharacterCreation()
    {
        const uint accountId = 42;

        StubRepository repository = new();
        CharacterLoginResolver resolver = new(repository);

        CharacterLoginResolution resolution = await resolver.ResolveAsync(accountId, TestContext.Current.CancellationToken);

        Assert.Equal(CharacterLoginRoute.CharacterCreation, resolution.Route);
        Assert.Null(resolution.Profile);
        Assert.Equal(accountId, repository.LastAccountId);
        Assert.Equal(1, repository.FindCallCount);
    }

    [Fact]
    public async Task ResolveAsync_ExistingCharacter_RoutesToExistingCharacter()
    {
        const uint accountId = 42;

        CharacterLoginProfile profile = CreateProfile(accountId);
        StubRepository repository = new(profile);
        CharacterLoginResolver resolver = new(repository);

        CharacterLoginResolution resolution = await resolver.ResolveAsync(accountId, TestContext.Current.CancellationToken);

        Assert.Equal(CharacterLoginRoute.ExistingCharacter, resolution.Route);
        Assert.Same(profile, resolution.Profile);
        Assert.Equal(accountId, repository.LastAccountId);
        Assert.Equal(1, repository.FindCallCount);
    }

    [Fact]
    public async Task ResolveAsync_ProfileBelongsToDifferentAccount_IsRejected()
    {
        CharacterLoginProfile profile = CreateProfile(accountId: 99);
        StubRepository repository = new(profile);
        CharacterLoginResolver resolver = new(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await resolver.ResolveAsync(accountId: 42, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, repository.FindCallCount);
    }

    [Fact]
    public async Task ResolveAsync_CancellationRequestedBeforeLookup_IsObserved()
    {
        StubRepository repository = new();
        CharacterLoginResolver resolver = new(repository);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await resolver.ResolveAsync(accountId: 42, cancellation.Token));

        Assert.Equal(0, repository.FindCallCount);
    }

    private static CharacterLoginProfile CreateProfile(uint accountId)
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, accountId, "Bernie"),
            new CharacterAppearance(composite: 1003, hair: 410),
            new CharacterProgression(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0),
            new CharacterAttributes(10, 10, 10, 10, 0),
            new CharacterVitals(100, 0),
            new CharacterEconomy(0, 0, 0),
            pkPoints: 0,
            titleId: 0,
            enlightenmentPoints: 0,
            new CharacterLocation(mapId: 1002, x: 430, y: 378));
    }

    private sealed class StubRepository(CharacterLoginProfile? profile = null) : ICharacterLoginProfileRepository
    {
        public int FindCallCount { get; private set; }
        public uint? LastAccountId { get; private set; }

        public ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FindCallCount++;
            LastAccountId = accountId;

            return ValueTask.FromResult(profile);
        }
    }
}
