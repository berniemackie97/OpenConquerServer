using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Accounts;

public sealed class AccountLoginInfrastructureServiceCollectionExtensionsTests
{
    private const ushort ActiveVerificationKeyId = 7;

    [Fact]
    public void AddAccountLoginInfrastructure_RejectsMissingConfiguration()
    {
        AccountLoginConnectionProtectionOptions connectionProtection = new();
        AccountAuthenticationProtectionOptions authenticationProtection = new();
        KeyValuePair<ushort, string>[] verificationKeys = CreateVerificationKeys();

        Assert.Throws<ArgumentNullException>(() => AccountLoginInfrastructureServiceCollectionExtensions.AddAccountLoginInfrastructure(null!, "Server=localhost", connectionProtection, authenticationProtection, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAccountLoginInfrastructure(" ", connectionProtection, authenticationProtection, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddAccountLoginInfrastructure("Server=localhost", null!, authenticationProtection, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddAccountLoginInfrastructure("Server=localhost", connectionProtection, null!, ActiveVerificationKeyId, verificationKeys));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddAccountLoginInfrastructure("Server=localhost", connectionProtection, authenticationProtection, ActiveVerificationKeyId, null!));
    }

    [Fact]
    public async Task AddAccountLoginInfrastructure_ComposesProductionLoginGraph()
    {
        AccountLoginConnectionProtectionOptions connectionProtectionOptions = new();
        AccountAuthenticationProtectionOptions authenticationProtectionOptions = new();

        await using ServiceProvider provider = new ServiceCollection()
            .AddAccountLoginInfrastructure("Server=localhost;Database=authentication", connectionProtectionOptions, authenticationProtectionOptions, ActiveVerificationKeyId, CreateVerificationKeys())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(connectionProtectionOptions, provider.GetRequiredService<AccountLoginConnectionProtectionOptions>());
        Assert.Same(authenticationProtectionOptions, provider.GetRequiredService<AccountAuthenticationProtectionOptions>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());

        AccountLoginConnectionProtection connectionProtection = provider.GetRequiredService<AccountLoginConnectionProtection>();
        Assert.Same(connectionProtection, provider.GetRequiredService<IAccountLoginConnectionLimiter>());

        AccountAuthenticationProtection authenticationProtection = provider.GetRequiredService<AccountAuthenticationProtection>();
        Assert.Same(authenticationProtection, provider.GetRequiredService<IAccountAuthenticationRequestLimiter>());
        Assert.Same(authenticationProtection, provider.GetRequiredService<IAccountAuthenticationAttemptLimiter>());

        Assert.IsType<AccountPasswordHasher>(provider.GetRequiredService<IAccountPasswordHasher>());
        Assert.IsType<AccountAuthenticator>(provider.GetRequiredService<IAccountAuthenticator>());
        Assert.IsType<GameLoginTicketGrantStore>(provider.GetRequiredService<IGameLoginTicketGrantStore>());
        Assert.IsType<CryptographicGameLoginTicketTokenGenerator>(provider.GetRequiredService<IGameLoginTicketTokenGenerator>());

        Assert.Same(provider.GetRequiredService<IAccountLoginConnectionLimiter>(), provider.GetRequiredService<IAccountLoginConnectionLimiter>());
        Assert.Same(provider.GetRequiredService<IAccountAuthenticator>(), provider.GetRequiredService<IAccountAuthenticator>());
        Assert.Same(provider.GetRequiredService<IGameLoginTicketGrantStore>(), provider.GetRequiredService<IGameLoginTicketGrantStore>());
        Assert.Same(provider.GetRequiredService<IGameLoginTicketTokenGenerator>(), provider.GetRequiredService<IGameLoginTicketTokenGenerator>());
        Assert.Same(provider.GetRequiredService<GameLoginTicketIssuer>(), provider.GetRequiredService<GameLoginTicketIssuer>());

        MySqlDataSource rawDataSource = provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey);
        Assert.Same(rawDataSource, provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey));
    }

    [Fact]
    public async Task AddAccountLoginInfrastructure_PreservesCallerTimeProvider()
    {
        TestTimeProvider timeProvider = new();
        ServiceCollection services = new();

        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddAccountLoginInfrastructure("Server=localhost;Database=authentication", new AccountLoginConnectionProtectionOptions(), new AccountAuthenticationProtectionOptions(), ActiveVerificationKeyId, CreateVerificationKeys());

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(timeProvider, provider.GetRequiredService<TimeProvider>());
        Assert.NotSame(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task AddAccountLoginInfrastructure_KeyRingIsContainerOwned()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddAccountLoginInfrastructure("Server=localhost;Database=authentication", new AccountLoginConnectionProtectionOptions(), new AccountAuthenticationProtectionOptions(), ActiveVerificationKeyId, CreateVerificationKeys())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        GameLoginTicketAuthenticationKeyRing keyRing = provider.GetRequiredService<GameLoginTicketAuthenticationKeyRing>();
        byte[] verifier = keyRing.CreateVerifier(123, 456);

        Assert.Equal(GameLoginTicketAuthenticationKeyVerifier.VerifierSize, verifier.Length);

        await provider.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => keyRing.CreateVerifier(123, 456));
    }

    [Fact]
    public void AddAccountLoginInfrastructure_RejectsInvalidVerificationKeyConfigurationBeforeRegistration()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() => services.AddAccountLoginInfrastructure(
            "Server=localhost;Database=authentication",
            new AccountLoginConnectionProtectionOptions(),
            new AccountAuthenticationProtectionOptions(),
            ActiveVerificationKeyId,
            [new KeyValuePair<ushort, string>(ActiveVerificationKeyId, "invalid")]));

        Assert.Empty(services);
    }

    [Fact]
    public void AddAccountLoginInfrastructure_RejectsMissingActiveVerificationKeyBeforeRegistration()
    {
        ServiceCollection services = [];

        Assert.Throws<ArgumentException>(() => services.AddAccountLoginInfrastructure(
            "Server=localhost;Database=authentication",
            new AccountLoginConnectionProtectionOptions(),
            new AccountAuthenticationProtectionOptions(),
            ActiveVerificationKeyId,
            CreateVerificationKeys(8)));

        Assert.Empty(services);
    }

    [Fact]
    public void AddAccountLoginInfrastructure_RejectsZeroActiveVerificationKeyIdBeforeRegistration()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddAccountLoginInfrastructure(
            "Server=localhost;Database=authentication",
            new AccountLoginConnectionProtectionOptions(),
            new AccountAuthenticationProtectionOptions(),
            0,
            CreateVerificationKeys()));

        Assert.Empty(services);
    }

    private static KeyValuePair<ushort, string>[] CreateVerificationKeys(ushort keyId = ActiveVerificationKeyId)
    {
        byte[] verificationKey = Enumerable.Range(1, GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize).Select(static value => checked((byte)value)).ToArray();

        return [new KeyValuePair<ushort, string>(keyId, Convert.ToBase64String(verificationKey))];
    }

    private sealed class TestTimeProvider : TimeProvider;
}
