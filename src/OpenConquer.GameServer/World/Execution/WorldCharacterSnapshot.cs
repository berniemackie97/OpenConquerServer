namespace OpenConquer.GameServer.World.Execution;

internal readonly record struct WorldCharacterSnapshot(WorldCharacterOwnership Ownership, ushort X, ushort Y, bool IsAlive = true);
