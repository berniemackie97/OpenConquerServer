using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountDatabaseFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4.11")
        .WithDatabase("authentication_tests")
        .Build();

    private ServiceProvider? _services;

    public ServiceProvider Services => _services
        ?? throw new InvalidOperationException("The account test database is not initialized.");

    public IDbContextFactory<AccountDbContext> ContextFactory => Services.GetRequiredService<IDbContextFactory<AccountDbContext>>();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            _services = new ServiceCollection()
                .AddAccountPersistence(_container.GetConnectionString())
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            await using AccountDbContext db = await ContextFactory.CreateDbContextAsync();
            string schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "accounts.sql"));
            await db.Database.ExecuteSqlRawAsync(schema);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_services is not null)
            {
                await _services.DisposeAsync();
            }
        }
        finally
        {
            _services = null;
            await _container.DisposeAsync();
        }
    }
}
