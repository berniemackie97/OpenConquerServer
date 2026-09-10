using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Accounts;

public sealed class GameLoginTicketRedemptionInfrastructureServiceCollectionExtensionsTests
{
    private const ushort ActiveVerificationKeyId = 7;

    [Fact]
    public void AddGameLoginTicketRedemptionInfrastructure_RejectsMissingConfiguration()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions protection = new();
        KeyValuePair<ushort, string>[] verificationKeys = CreateVerificationKeys();

        Assert.Throws<ArgumentNullException>(() => GameLoginTicketRedemptionInfrastructureServiceCollectionExtensions.AddGameLoginTicketRedemptionInfrastructure(null!, "Server=localhost", protection, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGameLoginTicketRedemptionInfrastructure(" ", protection, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddGameLoginTicketRedemptionInfrastructure("Server=localhost", null!, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddGameLoginTicketRedemptionInfrastructure("Server=localhost", protection, ActiveVerificationKeyId, null!));
    }

    [Fact]
    public async Task AddGameLoginTicketRedemptionInfrastructure_ComposesProductionRedemptionGraph()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions protection = new();

        await using ServiceProvider provider = new ServiceCollection()
            .AddGameLoginTicketRedemptionInfrastructure("Server=localhost;Database=authentication", protection, ActiveVerificationKeyId, CreateVerificationKeys())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(protection, provider.GetRequiredService<GameLoginTicketRedemptionAttemptLimiterOptions>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());

        GameLoginTicketRedemptionAttemptLimiter limiter = provider.GetRequiredService<GameLoginTicketRedemptionAttemptLimiter>();

        Assert.Same(limiter, provider.GetRequiredService<IGameLoginTicketRedemptionAttemptLimiter>());
        Assert.IsType<GameLoginTicketRedemptionStore>(provider.GetRequiredService<IGameLoginTicketRedemptionStore>());

        Assert.Same(provider.GetRequiredService<IGameLoginTicketRedemptionStore>(), provider.GetRequiredService<IGameLoginTicketRedemptionStore>());
        Assert.Same(provider.GetRequiredService<GameLoginTicketRedeemer>(), provider.GetRequiredService<GameLoginTicketRedeemer>());

        MySqlDataSource rawDataSource = provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey);

        Assert.Same(rawDataSource, provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey));

        Assert.Null(provider.GetService<IGameLoginTicketGrantStore>());
        Assert.Null(provider.GetService<IGameLoginTicketTokenGenerator>());
        Assert.Null(provider.GetService<GameLoginTicketIssuer>());
    }

    [Fact]
    public async Task AddGameLoginTicketRedemptionInfrastructure_PreservesCallerTimeProvider()
    {
        TestTimeProvider timeProvider = new();
        ServiceCollection services = new();

        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddGameLoginTicketRedemptionInfrastructure(
            "Server=localhost;Database=authentication",
            new GameLoginTicketRedemptionAttemptLimiterOptions(),
            ActiveVerificationKeyId,
            CreateVerificationKeys());

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(timeProvider, provider.GetRequiredService<TimeProvider>());
        Assert.NotSame(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task AddGameLoginTicketRedemptionInfrastructure_KeyRingIsContainerOwned()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddGameLoginTicketRedemptionInfrastructure(
                "Server=localhost;Database=authentication",
                new GameLoginTicketRedemptionAttemptLimiterOptions(),
                ActiveVerificationKeyId,
                CreateVerificationKeys())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        GameLoginTicketAuthenticationKeyRing keyRing = provider.GetRequiredService<GameLoginTicketAuthenticationKeyRing>();

        byte[] verifier = keyRing.CreateVerifier(123, 456);

        Assert.Equal(GameLoginTicketAuthenticationKeyVerifier.VerifierSize, verifier.Length);

        await provider.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => keyRing.CreateVerifier(123, 456));
    }

    [Fact]
    public async Task AddGameLoginTicketRedemptionInfrastructure_SnapshotsSingleUseVerificationKeyEnumerable()
    {
        IEnumerable<KeyValuePair<ushort, string>> verificationKeys = CreateSingleUseVerificationKeys();

        await using ServiceProvider provider = new ServiceCollection()
            .AddGameLoginTicketRedemptionInfrastructure("Server=localhost;Database=authentication",
                new GameLoginTicketRedemptionAttemptLimiterOptions(),
                ActiveVerificationKeyId,
                verificationKeys)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        GameLoginTicketAuthenticationKeyRing keyRing = provider.GetRequiredService<GameLoginTicketAuthenticationKeyRing>();
        byte[] verifier = keyRing.CreateVerifier(123, 456);

        Assert.Equal(GameLoginTicketAuthenticationKeyVerifier.VerifierSize, verifier.Length);
    }

    [Fact]
    public void AddGameLoginTicketRedemptionInfrastructure_RejectsInvalidVerificationKeyConfigurationBeforeRegistration()
    {
        ServiceCollection services = [];

        Assert.Throws<ArgumentException>(() => services.AddGameLoginTicketRedemptionInfrastructure("Server=localhost;Database=authentication",
            new GameLoginTicketRedemptionAttemptLimiterOptions(),
            ActiveVerificationKeyId,
            [new KeyValuePair<ushort, string>(ActiveVerificationKeyId, "invalid")]));

        Assert.Empty(services);
    }

    [Fact]
    public void AddGameLoginTicketRedemptionInfrastructure_RejectsMissingActiveVerificationKeyBeforeRegistration()
    {
        ServiceCollection services = [];

        Assert.Throws<ArgumentException>(() => services.AddGameLoginTicketRedemptionInfrastructure("Server=localhost;Database=authentication",
            new GameLoginTicketRedemptionAttemptLimiterOptions(),
            ActiveVerificationKeyId,
            CreateVerificationKeys(8)));

        Assert.Empty(services);
    }

    [Fact]
    public void AddGameLoginTicketRedemptionInfrastructure_RejectsZeroActiveVerificationKeyIdBeforeRegistration()
    {
        ServiceCollection services = [];

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddGameLoginTicketRedemptionInfrastructure(
            "Server=localhost;Database=authentication",
            new GameLoginTicketRedemptionAttemptLimiterOptions(),
            0,
            CreateVerificationKeys()));

        Assert.Empty(services);
    }

    private static KeyValuePair<ushort, string>[] CreateVerificationKeys(ushort keyId = ActiveVerificationKeyId)
    {
        byte[] verificationKey = Enumerable.Range(1, GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize).Select(static value => checked((byte)value)).ToArray();

        return [new KeyValuePair<ushort, string>(keyId, Convert.ToBase64String(verificationKey))];
    }

    private static IEnumerable<KeyValuePair<ushort, string>> CreateSingleUseVerificationKeys()
    {
        bool enumerated = false;

        return Enumerate();

        IEnumerable<KeyValuePair<ushort, string>> Enumerate()
        {
            if (enumerated)
            {
                throw new InvalidOperationException("Verification keys were enumerated more than once.");
            }

            enumerated = true;

            foreach (KeyValuePair<ushort, string> verificationKey in CreateVerificationKeys())
            {
                yield return verificationKey;
            }
        }
    }

    private sealed class TestTimeProvider : TimeProvider;
}
