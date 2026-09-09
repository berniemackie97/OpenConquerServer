using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountPersistenceTests
{
    [Fact]
    public void AddAccountPersistence_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => AccountPersistenceServiceCollectionExtensions.AddAccountPersistence(null!, "Server=localhost"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAccountPersistence(" "));
    }

    [Fact]
    public async Task AddAccountPersistence_UsesConfiguredProviderSemantics()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddAccountPersistence("Server=localhost;Database=authentication;UseAffectedRows=true;AutoEnlist=true")
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        IDbContextFactory<AccountDbContext> factory = services.GetRequiredService<IDbContextFactory<AccountDbContext>>();

        await using AccountDbContext first = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await using AccountDbContext second = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);

        string efConnectionString = Assert.IsType<string>(first.Database.GetConnectionString());
        MySqlConnectionStringBuilder efConnection = new(efConnectionString);

        Assert.False(efConnection.UseAffectedRows);
        Assert.True(efConnection.AutoEnlist);
        Assert.Equal(MySqlGuidFormat.Binary16, efConnection.GuidFormat);
        Assert.Equal(MySqlDateTimeKind.Utc, efConnection.DateTimeKind);

        Assert.Null(services.GetService<MySqlDataSource>());

        MySqlDataSource rawDataSource = services.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey);
        MySqlConnectionStringBuilder rawConnection = new(rawDataSource.ConnectionString);

        Assert.Same(
            rawDataSource,
            services.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey));

        Assert.False(rawConnection.UseAffectedRows);
        Assert.False(rawConnection.AutoEnlist);
        Assert.Equal(MySqlGuidFormat.Binary16, rawConnection.GuidFormat);
        Assert.Equal(MySqlDateTimeKind.Utc, rawConnection.DateTimeKind);

        Assert.Same(
            services.GetRequiredService<IAccountAuthenticationRepository>(),
            services.GetRequiredService<IAccountAuthenticationRepository>());

        Assert.Same(
            services.GetRequiredService<IAccountMutationStore>(),
            services.GetRequiredService<IAccountMutationStore>());

        Assert.Same(
            services.GetRequiredService<AccountDatabaseReadinessVerifier>(),
            services.GetRequiredService<AccountDatabaseReadinessVerifier>());

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }
}
