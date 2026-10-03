using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.World.Presence;

namespace OpenConquer.GameServer.Tests.World.Presence;

public sealed class CharacterPresenceDirectoryTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint OtherCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId + 1;

    [Fact]
    public void Register_MarksCharacterOnlineUntilRegistrationIsDisposed()
    {
        CharacterPresenceDirectory directory = new();

        Assert.False(directory.IsOnline(CharacterId));

        ICharacterPresenceLease registration = directory.Register(CharacterId);

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(registration.IsRevoked);
        Assert.False(registration.RevocationToken.IsCancellationRequested);

        registration.Dispose();

        Assert.False(directory.IsOnline(CharacterId));
        Assert.True(registration.IsRevoked);
        Assert.True(registration.RevocationToken.IsCancellationRequested);
    }

    [Fact]
    public void Register_TracksCharactersIndependently()
    {
        CharacterPresenceDirectory directory = new();
        using ICharacterPresenceLease first = directory.Register(CharacterId);
        ICharacterPresenceLease second = directory.Register(OtherCharacterId);

        Assert.True(directory.IsOnline(CharacterId));
        Assert.True(directory.IsOnline(OtherCharacterId));
        Assert.False(first.IsRevoked);
        Assert.False(second.IsRevoked);

        second.Dispose();

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(directory.IsOnline(OtherCharacterId));
        Assert.False(first.IsRevoked);
        Assert.True(second.IsRevoked);
    }

    [Fact]
    public void Register_NewerRegistrationRevokesAndSupersedesOlderRegistration()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease older = directory.Register(CharacterId);
        using ICharacterPresenceLease newer = directory.Register(CharacterId);

        Assert.True(older.IsRevoked);
        Assert.True(older.RevocationToken.IsCancellationRequested);
        Assert.False(newer.IsRevoked);
        Assert.False(newer.RevocationToken.IsCancellationRequested);
        Assert.True(directory.IsOnline(CharacterId));

        older.Dispose();

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(newer.IsRevoked);
    }

    [Fact]
    public void Register_DisplacedRevocationFailureRollsBackReplacementAndLeavesDirectoryUsable()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease older = directory.Register(CharacterId);
        InvalidOperationException callbackFailure = new("revocation callback failed");
        CancellationTokenRegistration callbackRegistration = older.RevocationToken.Register(() => throw callbackFailure);

        AggregateException exception = Assert.Throws<AggregateException>(() => directory.Register(CharacterId));

        callbackRegistration.Dispose();

        Assert.Contains(callbackFailure, exception.Flatten().InnerExceptions);
        Assert.True(older.IsRevoked);
        Assert.True(older.RevocationToken.IsCancellationRequested);
        Assert.False(directory.IsOnline(CharacterId));

        using ICharacterPresenceLease recovery = directory.Register(CharacterId);

        older.Dispose();

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(recovery.IsRevoked);
        Assert.False(recovery.RevocationToken.IsCancellationRequested);
    }

    [Fact]
    public void Register_DisposingCurrentRegistrationDoesNotRestoreSupersededRegistration()
    {
        CharacterPresenceDirectory directory = new();
        using ICharacterPresenceLease older = directory.Register(CharacterId);
        ICharacterPresenceLease newer = directory.Register(CharacterId);

        Assert.True(older.IsRevoked);

        newer.Dispose();

        Assert.True(newer.IsRevoked);
        Assert.False(directory.IsOnline(CharacterId));
    }

    [Fact]
    public void Registration_DisposeIsIdempotent()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease registration = directory.Register(CharacterId);

        registration.Dispose();
        registration.Dispose();

        Assert.True(registration.IsRevoked);
        Assert.True(registration.RevocationToken.IsCancellationRequested);
        Assert.False(directory.IsOnline(CharacterId));
    }

    [Fact]
    public void Registration_RevocationFailureStillReleasesPresenceAndRemainsIdempotent()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease registration = directory.Register(CharacterId);
        InvalidOperationException callbackFailure = new("revocation callback failed");
        CancellationTokenRegistration callbackRegistration = registration.RevocationToken.Register(() => throw callbackFailure);

        AggregateException exception = Assert.Throws<AggregateException>(() => registration.Dispose());

        callbackRegistration.Dispose();

        Assert.Contains(callbackFailure, exception.Flatten().InnerExceptions);
        Assert.True(registration.IsRevoked);
        Assert.True(registration.RevocationToken.IsCancellationRequested);
        Assert.False(directory.IsOnline(CharacterId));

        registration.Dispose();

        Assert.False(directory.IsOnline(CharacterId));

        using ICharacterPresenceLease recovery = directory.Register(CharacterId);

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(recovery.IsRevoked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Register_NonPlayerCharacterIdIsRejected(uint characterId)
    {
        CharacterPresenceDirectory directory = new();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => directory.Register(characterId));

        Assert.Equal("characterId", exception.ParamName);
        Assert.False(directory.IsOnline(CharacterId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void IsOnline_NonPlayerCharacterIdIsRejected(uint characterId)
    {
        CharacterPresenceDirectory directory = new();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => directory.IsOnline(characterId));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void ReaderCapability_ExposesPresenceWithoutRegistrationMutation()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceReader reader = directory;
        using ICharacterPresenceLease registration = directory.Register(CharacterId);

        Assert.True(reader.IsOnline(CharacterId));
    }

    [Fact]
    public void RegistrarCapability_RegistersPresenceWithoutRequiringConcreteDirectory()
    {
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceRegistrar registrar = directory;
        ICharacterPresenceReader reader = directory;

        using ICharacterPresenceLease registration = registrar.Register(CharacterId);

        Assert.True(reader.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ConcurrentRegistrations_LeaveExactlyOneAuthoritativeRegistration()
    {
        const int registrationCount = 64;
        CharacterPresenceDirectory directory = new();

        Task<ICharacterPresenceLease>[] registrationTasks = Enumerable.Range(0, registrationCount).Select(_ => Task.Run(() => directory.Register(CharacterId), TestContext.Current.CancellationToken)).ToArray();
        ICharacterPresenceLease[] registrations = await Task.WhenAll(registrationTasks);

        Assert.True(directory.IsOnline(CharacterId));

        ICharacterPresenceLease current = Assert.Single(registrations, static registration => !registration.IsRevoked);
        Assert.False(current.RevocationToken.IsCancellationRequested);
        Assert.All(registrations.Where(registration => !ReferenceEquals(registration, current)), static registration =>
        {
            Assert.True(registration.IsRevoked);
            Assert.True(registration.RevocationToken.IsCancellationRequested);
        });

        await Task.WhenAll(registrations.Select(registration => Task.Run(registration.Dispose, TestContext.Current.CancellationToken)));

        Assert.False(directory.IsOnline(CharacterId));
    }
}
