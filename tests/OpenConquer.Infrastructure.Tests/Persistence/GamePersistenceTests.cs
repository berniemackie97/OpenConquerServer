using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Extensions;
using OpenConquer.Infrastructure.Persistence.Game.Items;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Social;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GamePersistenceTests
{
    [Fact]
    public void AddGamePersistence_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => GamePersistenceServiceCollectionExtensions.AddGamePersistence(null!, "Server=localhost"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGamePersistence(" "));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddGamePersistence("Server=localhost", null!));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddGamePersistence("Server=localhost", new CharacterItemHydrationOptions(), null!));
    }

    [Fact]
    public async Task AddGamePersistence_UsesConfiguredProviderSemantics()
    {
        CharacterItemHydrationOptions itemOptions = new(maximumItemsPerCharacter: 512);
        CharacterSocialRelationHydrationOptions socialOptions = new(maximumRelationsPerCharacter: 256);

        await using ServiceProvider services = new ServiceCollection()
            .AddGamePersistence("Server=localhost;Database=game;UseAffectedRows=true;AutoEnlist=true", itemOptions, socialOptions)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        IDbContextFactory<GameDbContext> factory = services.GetRequiredService<IDbContextFactory<GameDbContext>>();

        await using GameDbContext first = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await using GameDbContext second = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);

        string efConnectionString = Assert.IsType<string>(first.Database.GetConnectionString());
        MySqlConnectionStringBuilder connection = new(efConnectionString);

        Assert.False(connection.UseAffectedRows);
        Assert.True(connection.AutoEnlist);
        Assert.Equal(MySqlGuidFormat.Binary16, connection.GuidFormat);
        Assert.Equal(MySqlDateTimeKind.Utc, connection.DateTimeKind);
        Assert.Null(services.GetService<MySqlDataSource>());

        Assert.Same(itemOptions, services.GetRequiredService<CharacterItemHydrationOptions>());
        Assert.Same(socialOptions, services.GetRequiredService<CharacterSocialRelationHydrationOptions>());

        ICharacterLoginProfileRepository characterRepository = services.GetRequiredService<ICharacterLoginProfileRepository>();
        Assert.Same(characterRepository, services.GetRequiredService<ICharacterLoginProfileRepository>());

        ICharacterItemSetRepository itemRepository = services.GetRequiredService<ICharacterItemSetRepository>();
        Assert.Same(itemRepository, services.GetRequiredService<ICharacterItemSetRepository>());

        ICharacterSocialRelationSetRepository socialRepository = services.GetRequiredService<ICharacterSocialRelationSetRepository>();
        Assert.Same(socialRepository, services.GetRequiredService<ICharacterSocialRelationSetRepository>());

        GameDatabaseReadinessVerifier readinessVerifier = services.GetRequiredService<GameDatabaseReadinessVerifier>();
        Assert.Same(readinessVerifier, services.GetRequiredService<GameDatabaseReadinessVerifier>());
        Assert.Same(readinessVerifier, services.GetRequiredService<IGameDatabaseReadinessVerifier>());

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }
}
