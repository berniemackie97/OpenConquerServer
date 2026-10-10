using OpenConquer.Assets.Maps;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class DMapTerrainAttachmentSource
{
    public static IEnumerable<MapTerrainAttachment> Enumerate(DMapScenerySection scenery, Func<string, TerrainObjectList> loadObjectList)
    {
        ArgumentNullException.ThrowIfNull(scenery);
        ArgumentNullException.ThrowIfNull(loadObjectList);

        foreach (DMapTerrainObjectGroup group in scenery.TerrainObjectGroups)
        {
            TerrainObjectList objects = loadObjectList(group.ListPath) ?? throw new InvalidDataException($"Terrain-object list '{group.ListPath}' could not be loaded.");

            foreach (TerrainObjectEntry entry in objects.Entries)
            {
                int anchorX = unchecked(group.AnchorTileX + entry.AnchorTileOffsetX);
                int anchorY = unchecked(group.AnchorTileY + entry.AnchorTileOffsetY);

                for (int row = 0; row < entry.Height; row++)
                {
                    for (int column = 0; column < entry.Width; column++)
                    {
                        TerrainObjectCell cell = entry.Cells[(row * entry.Width) + column];

                        yield return new MapTerrainAttachment(unchecked(anchorX - column), unchecked(anchorY - row), cell.SurfaceId, cell.PassabilityFlag, cell.ElevationDelta);
                    }
                }
            }
        }
    }
}
