namespace OpenConquer.GameServer.Configuration;

internal sealed class GameServerSettings
{
    public ContentSettings Content { get; set; } = new();
    public WorldSettings World { get; set; } = new();
    public GameLoginTicketSettings GameLoginTickets { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public ItemContentSettings Items { get; set; } = new();

    internal sealed class ContentSettings
    {
        public string? DefinitionsPath
        {
            get; set;
        }

        public string? TerrainDirectory
        {
            get; set;
        }

        public string? CollisionDirectory
        {
            get; set;
        }

        public int MaximumDefinitionFileBytes
        {
            get; set;
        }

        public int MaximumDefinitions
        {
            get; set;
        }
    }

    internal sealed class WorldSettings
    {
        public int WorkerCount
        {
            get; set;
        }

        public int PartitionQueueCapacity
        {
            get; set;
        }

        public int MaxCommandsPerTurn
        {
            get; set;
        }

        public int MaxCharacters
        {
            get; set;
        }

        public int MaxCharactersPerMap
        {
            get; set;
        }
    }

    internal sealed class GameLoginTicketSettings
    {
        public ushort ActiveVerificationKeyId
        {
            get; set;
        }

        public List<VerificationKeySettings> VerificationKeys { get; set; } = [];
    }

    internal sealed class VerificationKeySettings
    {
        public ushort Id
        {
            get; set;
        }

        public string? EncodedKey
        {
            get; set;
        }
    }

    internal sealed class NetworkSettings
    {
        public bool Enabled
        {
            get; set;
        }

        public string? BindAddress
        {
            get; set;
        }

        public int Port
        {
            get; set;
        }

        public int ListenBacklog
        {
            get; set;
        }

        public int AdmissionCapacity
        {
            get; set;
        }

        public int MaximumConnectionsPerSource
        {
            get; set;
        }

        public int WorkerCount
        {
            get; set;
        }

        public TimeSpan ConnectionTimeout
        {
            get; set;
        }
    }

    internal sealed class ItemContentSettings
    {
        public string? FilePath
        {
            get; set;
        }

        public int MaximumFileBytes
        {
            get; set;
        }

        public int MaximumDefinitions
        {
            get; set;
        }
    }
}
