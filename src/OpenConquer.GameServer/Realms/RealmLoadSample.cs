using OpenConquer.Domain.Realms;

namespace OpenConquer.GameServer.Realms;

internal readonly record struct RealmLoadSample(RealmLoad Load, int OnlineCount);
