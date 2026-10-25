using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameServerCharacterCreationPersistenceTests
{
    [Fact]
    public void ConfigureServices_WithoutStartingStateNeverRegistersCreationWrites()
    {
        ServiceCollection services = new();

        GameServerHost.ConfigureServices(services, GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath()));

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(ICharacterCreationStore));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(CharacterCreationService));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(CharacterCreationStartingState));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(GameCharacterCreationLoginProcessor));
    }

    [Fact]
    public void ConfigureServices_StartingStateRegistersStoreAndApplicationService()
    {
        ServiceCollection services = new();
        GameServerConfiguration configuration = GameServerCharacterCreationConfigurationTests.LoadWithStartingState();

        GameServerHost.ConfigureServices(services, configuration);

        Assert.Single(services, service => service.ServiceType == typeof(ICharacterCreationStore));
        Assert.Single(services, service => service.ServiceType == typeof(CharacterCreationService));
        Assert.Single(services, service => service.ServiceType == typeof(CharacterCreationStartingState));
        Assert.Single(services, service => service.ServiceType == typeof(GameCharacterCreationLoginProcessor));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CharacterCreationStore>(provider.GetRequiredService<ICharacterCreationStore>());
        Assert.Same(configuration.CharacterCreationStartingState, provider.GetRequiredService<CharacterCreationStartingState>());
        Assert.IsType<CharacterCreationService>(provider.GetRequiredService<CharacterCreationService>());
    }
}
