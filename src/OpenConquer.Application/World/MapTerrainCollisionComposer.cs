using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public static class MapTerrainCollisionComposer
{
    public static MapTerrainCollision Compose(MapBaseTerrain terrain, IEnumerable<MapTerrainAttachment> attachments, long maximumAttachments)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAttachments);

        Dictionary<int, MapBaseTerrainCell> effectiveCells = [];
        long processed = 0;

        foreach (MapTerrainAttachment attachment in attachments)
        {
            processed++;

            if (processed > maximumAttachments)
            {
                throw new InvalidDataException($"Terrain attachment count exceeds the configured maximum of {maximumAttachments}.");
            }

            if (attachment.X < 0 || attachment.Y < 0 || attachment.X >= terrain.Width || attachment.Y >= terrain.Height)
            {
                continue;
            }

            int index = (attachment.Y * terrain.Width) + attachment.X;

            MapBaseTerrainCell previous = effectiveCells.TryGetValue(index, out MapBaseTerrainCell current)
                ? current
                : terrain.Cells[index];

            short elevation = unchecked((short)(previous.Elevation + attachment.ElevationDelta));

            effectiveCells[index] = new MapBaseTerrainCell(attachment.SurfaceId, attachment.PassabilityFlag, elevation);
        }

        IEnumerable<MapTerrainCollisionOverride> overrides = effectiveCells
            .Where(entry => entry.Value != terrain.Cells[entry.Key])
            .Select(entry => new MapTerrainCollisionOverride(entry.Key, entry.Value));

        return new MapTerrainCollision(terrain, overrides);
    }
}
