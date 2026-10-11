using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps.Collision;

internal sealed record MapCollisionSourceBuild(MapCollisionSourceReport Report, IReadOnlyList<MapTerrainCollision> Collisions);
