using OpenConquer.Domain.Social;

namespace OpenConquer.Infrastructure.Persistence.Game.Social;

internal sealed class SocialRelationRecord
{
    public uint OwnerCharacterId { get; set; }
    public uint CounterpartCharacterId { get; set; }
    public SocialRelationKind Kind { get; set; }
}
