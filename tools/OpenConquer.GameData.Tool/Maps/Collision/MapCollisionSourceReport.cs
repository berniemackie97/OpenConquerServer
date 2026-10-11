namespace OpenConquer.GameData.Tool.Maps.Collision;

internal sealed record MapCollisionSourceReport(IReadOnlyList<MapCollisionSourceResult> Terrains, long TotalAttachments, long TotalObjectBytes)
{
    public int TerrainCount => Terrains.Count;
    public long TotalGroups => Terrains.Sum(terrain => (long)terrain.TerrainObjectGroups);
    public long TotalEffectiveOverrides => Terrains.Sum(terrain => (long)terrain.EffectiveOverrides);
}
