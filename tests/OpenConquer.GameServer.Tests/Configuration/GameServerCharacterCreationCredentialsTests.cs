using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameServerCharacterCreationCredentialsTests
{
    private const string GameReader = "Server=localhost;Database=game;User ID=game_reader;Password=reader_password";
    private const string GameCreator = "Server=localhost;Database=game;User ID=game_creator;Password=creator_password";

    [Fact]
    public void Load_DefaultConfigurationHasNoCreationCredential()
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath());

        Assert.Null(configuration.CharacterCreationConnectionString);
    }

    [Fact]
    public void Load_ValidSeparateCredentialRequiresStartingState()
    {
        GameServerConfiguration configuration = LoadWithCredential();

        Assert.Equal(GameCreator, configuration.CharacterCreationConnectionString);
        Assert.NotNull(configuration.CharacterCreationStartingState);
    }

    [Fact]
    public void Load_StandaloneCredentialWithoutStartingStateIsRejected()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            GameServerConfiguration.Load(GameServerConfigurationTests.CreateConfiguration(values =>
                values["ConnectionStrings:GameCreation"] = GameCreator), Path.GetTempPath()));

        Assert.Contains("starting state", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Load_BlankSuppliedCredentialIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => LoadWithCredential(values =>
            values["ConnectionStrings:GameCreation"] = value));
    }

    [Theory]
    [InlineData("Server=localhost;Database=game;User ID=game_reader;Password=other")]
    [InlineData("Server=localhost;Database=game;User ID=root;Password=other")]
    [InlineData("Server=localhost;Database=another_game;User ID=game_creator;Password=other")]
    [InlineData("Server=other.example;Database=game;User ID=game_creator;Password=other")]
    public void Load_NonDedicatedCredentialIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => LoadWithCredential(values =>
            values["ConnectionStrings:GameCreation"] = value));
    }

    [Fact]
    public void Load_MalformedCreationCredentialDoesNotLeakSecret()
    {
        const string malformed = "Server=localhost;Database=game;Password=super_private;UnrecognizedConnectionOption=1";

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            LoadWithCredential(values => values["ConnectionStrings:GameCreation"] = malformed));

        Assert.DoesNotContain("super_private", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_DefaultAndStartingStateOnlyNeverRegisterCreationWrites()
    {
        AssertNotRegistered(GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath()));
        AssertNotRegistered(GameServerCharacterCreationConfigurationTests.LoadWithStartingState());
    }

    [Fact]
    public void ConfigureServices_ExplicitCredentialRegistersStoreAndApplicationService()
    {
        ServiceCollection services = new();
        GameServerConfiguration configuration = LoadWithCredential();

        GameServerHost.ConfigureServices(services, configuration);

        Assert.Single(services, service => service.ServiceType == typeof(ICharacterCreationStore));
        Assert.Single(services, service => service.ServiceType == typeof(CharacterCreationService));
        Assert.Single(services, service => service.ServiceType == typeof(CharacterCreationStartingState));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CharacterCreationStore>(provider.GetRequiredService<ICharacterCreationStore>());
        Assert.Same(configuration.CharacterCreationStartingState, provider.GetRequiredService<CharacterCreationStartingState>());
        Assert.IsType<CharacterCreationService>(provider.GetRequiredService<CharacterCreationService>());
    }

    private static void AssertNotRegistered(GameServerConfiguration configuration)
    {
        ServiceCollection services = new();
        GameServerHost.ConfigureServices(services, configuration);

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(ICharacterCreationStore));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(CharacterCreationService));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(CharacterCreationStartingState));
    }

    private static GameServerConfiguration LoadWithCredential(Action<Dictionary<string, string?>>? mutate = null)
    {
        return GameServerCharacterCreationConfigurationTests.LoadWithStartingState(values =>
        {
            values["ConnectionStrings:Game"] = GameReader;
            values["ConnectionStrings:GameCreation"] = GameCreator;
            mutate?.Invoke(values);
        });
    }
}
