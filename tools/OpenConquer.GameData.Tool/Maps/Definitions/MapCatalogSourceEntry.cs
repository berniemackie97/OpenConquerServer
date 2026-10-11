namespace OpenConquer.GameData.Tool.Maps.Definitions;

internal sealed record MapCatalogSourceEntry(uint MapId, uint? MapDataId, ulong? SourceFlags,
    ulong? ClientFlags, string Status, string Confidence, string MapDataSource,
    string FlagsBasis, string Reason, string? TerrainPath);
