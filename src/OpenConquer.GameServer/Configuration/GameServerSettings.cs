namespace OpenConquer.GameServer.Configuration;

internal sealed class GameServerSettings
{
    public ContentSettings Content { get; set; } = new();
    public WorldSettings World { get; set; } = new();
    public GameLoginTicketSettings GameLoginTickets { get; set; } = new();

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
}
