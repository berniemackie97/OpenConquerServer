using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.World;
using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.Infrastructure.Content.Items;
using OpenConquer.Infrastructure.Content.Maps;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.GameServer.Configuration;

/// <summary>
/// Validates GameServer deployment configuration before constructing infrastructure or starting the world runtime.
/// </summary>
internal sealed class GameServerConfiguration
{
    public const string SectionName = "GameServer";

    private GameServerConfiguration(RealmId realmId, string accountConnectionString, string gameConnectionString,
        MapDefinitionCatalogFileOptions mapDefinitions, string terrainDirectory, string collisionDirectory,
        MapTerrainLoadLimits terrainLimits, MapCollisionLoadLimits collisionLimits, WorldExecutionOptions world,
        GameNetworkOptions? network, RealmStatusOptions? realmStatus, ItemTypeCatalogFileOptions? itemTypes,
        ushort activeVerificationKeyId, KeyValuePair<ushort, string>[] verificationKeys,
        CharacterCreationStartingState? characterCreationStartingState, string? characterCreationConnectionString,
        string transitionsPath, int maximumTransitionFileBytes, int maximumTransitions)
    {
        RealmId = realmId;
        AccountConnectionString = accountConnectionString;
        GameConnectionString = gameConnectionString;
        MapDefinitions = mapDefinitions;
        TerrainDirectory = terrainDirectory;
        CollisionDirectory = collisionDirectory;
        TerrainLimits = terrainLimits;
        CollisionLimits = collisionLimits;
        World = world;
        Network = network;
        RealmStatus = realmStatus;
        ItemTypes = itemTypes;
        ActiveVerificationKeyId = activeVerificationKeyId;
        VerificationKeys = Array.AsReadOnly(verificationKeys);
        CharacterCreationStartingState = characterCreationStartingState;
        CharacterCreationConnectionString = characterCreationConnectionString;
        TransitionsPath = transitionsPath;
        MaximumTransitionFileBytes = maximumTransitionFileBytes;
        MaximumTransitions = maximumTransitions;
    }

    public RealmId RealmId { get; }
    public string AccountConnectionString { get; }
    public string GameConnectionString { get; }
    public MapDefinitionCatalogFileOptions MapDefinitions { get; }
    public string TerrainDirectory { get; }
    public string CollisionDirectory { get; }
    public MapTerrainLoadLimits TerrainLimits { get; }
    public MapCollisionLoadLimits CollisionLimits { get; }
    public WorldExecutionOptions World { get; }
    public GameNetworkOptions? Network { get; }

    /// <summary>
    /// Realm status reporting settings. Set only when network ingress is enabled, because a GameServer that accepts
    /// no logins must not be reported as available.
    /// </summary>
    public RealmStatusOptions? RealmStatus { get; }
    public ItemTypeCatalogFileOptions? ItemTypes { get; }
    public ushort ActiveVerificationKeyId { get; }
    public IReadOnlyList<KeyValuePair<ushort, string>> VerificationKeys { get; }
    public CharacterCreationStartingState? CharacterCreationStartingState { get; }
    public string? CharacterCreationConnectionString { get; }
    public string TransitionsPath { get; }
    public int MaximumTransitionFileBytes { get; }
    public int MaximumTransitions { get; }

    public static GameServerConfiguration Load(IConfiguration configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        string accountConnectionString = configuration.GetConnectionString("Accounts") ?? string.Empty;
        string gameConnectionString = configuration.GetConnectionString("Game") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(accountConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Accounts is missing or empty.");
        }

        if (string.IsNullOrWhiteSpace(gameConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Game is missing or empty.");
        }

        GameServerSettings settings = new();
        configuration.GetRequiredSection(SectionName).Bind(settings, options => options.ErrorOnUnknownConfiguration = true);

        try
        {
            RealmId realmId = new(settings.RealmId);
            string root = Path.GetFullPath(contentRootPath);
            GameServerSettings.ContentSettings content = settings.Content ?? throw new ArgumentException("GameServer content configuration is missing.");
            GameServerSettings.WorldSettings worldSettings = settings.World ?? throw new ArgumentException("GameServer world configuration is missing.");
            GameServerSettings.GameLoginTicketSettings ticketSettings = settings.GameLoginTickets ?? throw new ArgumentException("GameServer ticket-verification configuration is missing.");
            GameServerSettings.NetworkSettings networkSettings = settings.Network ?? throw new ArgumentException("GameServer network configuration is missing.");

            MapDefinitionCatalogFileOptions definitions = new(
                ResolvePath(content.DefinitionsPath, root, $"{SectionName}:Content:DefinitionsPath"),
                ValidatePositive(content.MaximumDefinitionFileBytes, 64 * 1024 * 1024, $"{SectionName}:Content:MaximumDefinitionFileBytes"),
                ValidatePositive(content.MaximumDefinitions, 100_000, $"{SectionName}:Content:MaximumDefinitions"));

            string terrainDirectory = ResolvePath(content.TerrainDirectory, root, $"{SectionName}:Content:TerrainDirectory");
            string collisionDirectory = ResolvePath(content.CollisionDirectory, root, $"{SectionName}:Content:CollisionDirectory");
            string transitionsPath = ResolvePath(content.TransitionsPath, root, $"{SectionName}:Content:TransitionsPath");
            int maximumTransitionFileBytes = ValidatePositive(content.MaximumTransitionFileBytes,
                64 * 1024 * 1024, $"{SectionName}:Content:MaximumTransitionFileBytes");
            int maximumTransitions = ValidatePositive(content.MaximumTransitions,
                100_000, $"{SectionName}:Content:MaximumTransitions");

            int maximumCharacters = ValidatePositive(worldSettings.MaxCharacters, 1_000_000, $"{SectionName}:World:MaxCharacters");
            int maximumCharactersPerMap = ValidatePositive(worldSettings.MaxCharactersPerMap, maximumCharacters, $"{SectionName}:World:MaxCharactersPerMap");

            WorldExecutionOptions world = new(
                ValidatePositive(worldSettings.WorkerCount, 1024, $"{SectionName}:World:WorkerCount"),
                ValidatePositive(worldSettings.PartitionQueueCapacity, 65_536, $"{SectionName}:World:PartitionQueueCapacity"),
                ValidatePositive(worldSettings.MaxCommandsPerTurn, 4096, $"{SectionName}:World:MaxCommandsPerTurn"),
                maximumCharacters, maximumCharactersPerMap);

            if (ticketSettings.ActiveVerificationKeyId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticketSettings.ActiveVerificationKeyId),
                    $"{SectionName}:GameLoginTickets:ActiveVerificationKeyId must be nonzero.");
            }

            KeyValuePair<ushort, string>[] keys = CreateVerificationKeys(ticketSettings.VerificationKeys);
            GameNetworkOptions? network = null;
            RealmStatusOptions? realmStatus = null;
            ItemTypeCatalogFileOptions? itemTypes = null;

            if (networkSettings.Enabled)
            {
                if (!IPAddress.TryParse(networkSettings.BindAddress, out IPAddress? bindAddress)
                    || bindAddress.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new ArgumentException($"{SectionName}:Network:BindAddress must be a valid IPv4 address.");
                }

                network = new GameNetworkOptions(new IPEndPoint(bindAddress, networkSettings.Port),
                    networkSettings.ListenBacklog, networkSettings.AdmissionCapacity,
                    networkSettings.MaximumConnectionsPerSource, networkSettings.WorkerCount, networkSettings.ConnectionTimeout);

                GameServerSettings.RealmStatusSettings realmStatusSettings = settings.RealmStatus
                    ?? throw new ArgumentException("GameServer realm status configuration is missing.");

                realmStatus = new RealmStatusOptions(realmStatusSettings.ReportInterval);

                GameServerSettings.ItemContentSettings itemSettings = settings.Items
                    ?? throw new ArgumentException("GameServer item-type content configuration is missing.");

                itemTypes = new ItemTypeCatalogFileOptions(
                    ResolvePath(itemSettings.FilePath, root, $"{SectionName}:Items:FilePath"),
                    ValidatePositive(itemSettings.MaximumFileBytes, 64 * 1024 * 1024, $"{SectionName}:Items:MaximumFileBytes"),
                    ValidatePositive(itemSettings.MaximumDefinitions, 1_000_000, $"{SectionName}:Items:MaximumDefinitions"));
            }

            CharacterCreationStartingState? characterCreationStartingState = CreateCharacterCreationStartingState(settings.CharacterCreation);
            string? characterCreationConnectionString = configuration.GetConnectionString("GameCreation");

            if (characterCreationConnectionString is not null)
            {
                if (string.IsNullOrWhiteSpace(characterCreationConnectionString))
                {
                    throw new InvalidOperationException("ConnectionStrings:GameCreation must not be empty when supplied.");
                }

                if (characterCreationStartingState is null)
                {
                    throw new InvalidOperationException("ConnectionStrings:GameCreation requires a validated GameServer:CharacterCreation starting state.");
                }

                GameCharacterCreationConnectionPolicy.Validate(gameConnectionString, characterCreationConnectionString);
            }

            return new GameServerConfiguration(realmId, accountConnectionString, gameConnectionString, definitions, terrainDirectory,
                collisionDirectory, MapTerrainLoadLimits.CreateDefault(), MapCollisionLoadLimits.CreateDefault(), world,
                network, realmStatus, itemTypes, ticketSettings.ActiveVerificationKeyId, keys, characterCreationStartingState,
                characterCreationConnectionString, transitionsPath, maximumTransitionFileBytes, maximumTransitions);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or NotSupportedException)
        {
            throw new InvalidOperationException($"Configuration section '{SectionName}' is invalid.", exception);
        }
    }

    private static CharacterCreationStartingState? CreateCharacterCreationStartingState(GameServerSettings.CharacterCreationSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(settings.Allotments);

        CharacterCreationStartingAllotment[] allotments = settings.Allotments.Select(row =>
            new CharacterCreationStartingAllotment(row.Family, row.Strength, row.Agility, row.Vitality, row.Spirit)).ToArray();

        return new CharacterCreationStartingState(settings.StartingMapId, settings.StartingX, settings.StartingY, allotments);
    }

    private static string ResolvePath(string? value, string root, string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{configurationPath} must contain a nonempty path.", nameof(value));
        }

        return Path.GetFullPath(value, root);
    }

    private static int ValidatePositive(int value, int maximum, string configurationPath)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{configurationPath} must be between 1 and {maximum}.");
        }

        return value;
    }

    private static KeyValuePair<ushort, string>[] CreateVerificationKeys(IReadOnlyCollection<GameServerSettings.VerificationKeySettings> configuredKeys)
    {
        ArgumentNullException.ThrowIfNull(configuredKeys);

        if (configuredKeys.Count == 0)
        {
            throw new ArgumentException($"{SectionName}:GameLoginTickets:VerificationKeys must contain at least one key.", nameof(configuredKeys));
        }

        KeyValuePair<ushort, string>[] keys = new KeyValuePair<ushort, string>[configuredKeys.Count];
        HashSet<ushort> identities = [];
        int index = 0;

        foreach (GameServerSettings.VerificationKeySettings key in configuredKeys)
        {
            if (key.Id == 0 || !identities.Add(key.Id))
            {
                throw new ArgumentException("Game-login verification key identities must be nonzero and unique.", nameof(configuredKeys));
            }

            if (string.IsNullOrWhiteSpace(key.EncodedKey))
            {
                throw new ArgumentException($"Game-login verification key {key.Id} has no encoded key material.", nameof(configuredKeys));
            }

            keys[index++] = new KeyValuePair<ushort, string>(key.Id, key.EncodedKey);
        }

        return keys;
    }
}
