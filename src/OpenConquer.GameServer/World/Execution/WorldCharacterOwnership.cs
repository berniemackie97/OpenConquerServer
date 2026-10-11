namespace OpenConquer.GameServer.World.Execution;

internal readonly record struct WorldCharacterOwnership(uint CharacterId, uint MapId, ulong Generation, Guid RuntimeId = default);
