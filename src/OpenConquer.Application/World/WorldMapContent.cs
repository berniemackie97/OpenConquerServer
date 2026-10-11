using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class WorldMapContent
{
    public WorldMapContent(MapDefinition definition, MapTerrainCollision collision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(collision);

        if (definition.MapDataId != collision.MapDataId)
        {
            throw new ArgumentException($"World map {definition.MapId} requires MapDataId {definition.MapDataId}, but collision content belongs to {collision.MapDataId}.", nameof(collision));
        }

        Definition = definition;
        Collision = collision;
    }

    public MapDefinition Definition
    {
        get;
    }

    public MapTerrainCollision Collision
    {
        get;
    }
    public MapBaseTerrain BaseTerrain => Collision.BaseTerrain;
    public uint MapId => Definition.MapId;
    public uint MapDataId => Definition.MapDataId;
    public ulong Flags => Definition.Flags;
}
