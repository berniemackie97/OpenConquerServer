using System.Collections.ObjectModel;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Social;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterFriendListWireProjection
{
    private readonly ReadOnlyCollection<GameSocialRelationPacket1019> _relationPackets;

    private ExistingCharacterFriendListWireProjection(CharacterSocialRelationSet runtimeRelationSet, GameSocialRelationPacket1019[] relationPackets)
    {
        RuntimeRelationSet = runtimeRelationSet;
        _relationPackets = Array.AsReadOnly(relationPackets);
    }

    public CharacterSocialRelationSet RuntimeRelationSet { get; }
    public IReadOnlyList<GameSocialRelationPacket1019> RelationPackets => _relationPackets;

    public static ExistingCharacterFriendListWireProjection Create(CharacterSocialRelationSet relationSet, ICharacterPresenceReader presenceReader)
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        ArgumentNullException.ThrowIfNull(presenceReader);

        GameSocialRelationPacket1019[] relationPackets = new GameSocialRelationPacket1019[relationSet.Count];

        for (int index = 0; index < relationSet.Count; index++)
        {
            CharacterSocialRelation relation = relationSet.Relations[index];

            GameSocialRelationAction action = relation.Kind switch
            {
                SocialRelationKind.Friend => GameSocialRelationAction.AddFriendSilently,
                SocialRelationKind.Enemy => GameSocialRelationAction.AddEnemy,
                _ => throw new InvalidDataException($"Social relation to character {relation.CounterpartCharacterId} contains unsupported relation kind {(byte)relation.Kind}."),
            };

            byte stateFlag = presenceReader.IsOnline(relation.CounterpartCharacterId) ? (byte)1 : (byte)0;

            relationPackets[index] = new GameSocialRelationPacket1019(relation.CounterpartCharacterId, action, stateFlag, peerageRank: 0, peerageSex: 0, relation.CounterpartName);
        }

        return new ExistingCharacterFriendListWireProjection(relationSet, relationPackets);
    }
}
