using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountPersistenceTests
{
    [Fact]
    public void AddAccountPersistence_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AccountPersistenceServiceCollectionExtensions.AddAccountPersistence(
                null!,
                "Server=localhost"
            )
        );

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAccountPersistence(" "));
    }

    [Fact]
    public async Task AddAccountPersistence_UsesConfiguredProviderSemantics()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddAccountPersistence("Server=localhost;Database=authentication;UseAffectedRows=true")
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            );

        IDbContextFactory<AccountDbContext> factory = services.GetRequiredService<
            IDbContextFactory<AccountDbContext>
        >();

        await using AccountDbContext first = await factory.CreateDbContextAsync(
            TestContext.Current.CancellationToken
        );

        await using AccountDbContext second = await factory.CreateDbContextAsync(
            TestContext.Current.CancellationToken
        );

        Assert.NotSame(first, second);

        string connectionString = Assert.IsType<string>(first.Database.GetConnectionString());

        MySqlConnectionStringBuilder connection = new(connectionString);

        Assert.False(connection.UseAffectedRows);
        Assert.Equal(MySqlGuidFormat.Binary16, connection.GuidFormat);
        Assert.Equal(MySqlDateTimeKind.Utc, connection.DateTimeKind);

        Assert.Same(
            services.GetRequiredService<IAccountAuthenticationRepository>(),
            services.GetRequiredService<IAccountAuthenticationRepository>()
        );

        Assert.Same(
            services.GetRequiredService<AccountDatabaseReadinessVerifier>(),
            services.GetRequiredService<AccountDatabaseReadinessVerifier>()
        );

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }
}
