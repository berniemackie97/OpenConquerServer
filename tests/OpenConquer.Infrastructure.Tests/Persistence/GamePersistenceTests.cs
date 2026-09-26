using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Extensions;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GamePersistenceTests
{
    [Fact]
    public void AddGamePersistence_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GamePersistenceServiceCollectionExtensions.AddGamePersistence(null!, "Server=localhost")
        );

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGamePersistence(" "));
    }

    [Fact]
    public async Task AddGamePersistence_UsesConfiguredProviderSemantics()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddGamePersistence(
                "Server=localhost;Database=game;UseAffectedRows=true;AutoEnlist=true"
            )
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            );

        IDbContextFactory<GameDbContext> factory = services.GetRequiredService<
            IDbContextFactory<GameDbContext>
        >();

        await using GameDbContext first = await factory.CreateDbContextAsync(
            TestContext.Current.CancellationToken
        );

        await using GameDbContext second = await factory.CreateDbContextAsync(
            TestContext.Current.CancellationToken
        );

        Assert.NotSame(first, second);

        string efConnectionString = Assert.IsType<string>(first.Database.GetConnectionString());

        MySqlConnectionStringBuilder connection = new(efConnectionString);

        Assert.False(connection.UseAffectedRows);
        Assert.True(connection.AutoEnlist);
        Assert.Equal(MySqlGuidFormat.Binary16, connection.GuidFormat);
        Assert.Equal(MySqlDateTimeKind.Utc, connection.DateTimeKind);

        Assert.Null(services.GetService<MySqlDataSource>());

        ICharacterLoginProfileRepository repository =
            services.GetRequiredService<ICharacterLoginProfileRepository>();

        Assert.Same(repository, services.GetRequiredService<ICharacterLoginProfileRepository>());

        GameDatabaseReadinessVerifier readinessVerifier =
            services.GetRequiredService<GameDatabaseReadinessVerifier>();

        Assert.Same(
            readinessVerifier,
            services.GetRequiredService<GameDatabaseReadinessVerifier>()
        );

        Assert.Same(
            readinessVerifier,
            services.GetRequiredService<IGameDatabaseReadinessVerifier>()
        );

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }
}
