using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterFriendListWireProjectionTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint FriendAId = CharacterId + 1;
    private const uint FriendBId = CharacterId + 2;
    private const uint EnemyAId = CharacterId + 3;
    private const uint EnemyBId = CharacterId + 4;

    [Fact]
    public void Create_NullDependencies_AreRejected()
    {
        CharacterSocialRelationSet relationSet = new(CharacterId, []);
        FakePresenceReader presence = new();

        Assert.Throws<ArgumentNullException>(() => ExistingCharacterFriendListWireProjection.Create(null!, presence));
        Assert.Throws<ArgumentNullException>(() => ExistingCharacterFriendListWireProjection.Create(relationSet, null!));
    }

    [Fact]
    public void Create_EmptyRelationSet_ReturnsEmptyProjection()
    {
        CharacterSocialRelationSet relationSet = new(CharacterId, []);
        FakePresenceReader presence = new();

        ExistingCharacterFriendListWireProjection projection = ExistingCharacterFriendListWireProjection.Create(relationSet, presence);

        Assert.Same(relationSet, projection.RuntimeRelationSet);
        Assert.Empty(projection.RelationPackets);
        Assert.Empty(presence.RequestedCharacterIds);
    }

    [Fact]
    public void Create_MixedRelations_MapsNativeActionsPresenceNamesPeerageAndDeterministicOrder()
    {
        CharacterSocialRelationSet relationSet = new(CharacterId,
        [
            CreateRelation(EnemyBId, SocialRelationKind.Enemy, "EnemyB"),
            CreateRelation(FriendBId, SocialRelationKind.Friend, "FriendB"),
            CreateRelation(EnemyAId, SocialRelationKind.Enemy, "EnemyA"),
            CreateRelation(FriendAId, SocialRelationKind.Friend, "FriendA"),
        ]);
        FakePresenceReader presence = new(FriendAId, EnemyBId);

        ExistingCharacterFriendListWireProjection projection = ExistingCharacterFriendListWireProjection.Create(relationSet, presence);

        Assert.Same(relationSet, projection.RuntimeRelationSet);
        Assert.Equal(4, projection.RelationPackets.Count);
        Assert.Equal([FriendAId, FriendBId, EnemyAId, EnemyBId], projection.RelationPackets.Select(static packet => packet.EntityId));
        Assert.Equal([FriendAId, FriendBId, EnemyAId, EnemyBId], presence.RequestedCharacterIds);

        AssertPacket(projection.RelationPackets[0], FriendAId, GameSocialRelationAction.AddFriendSilently, stateFlag: 1, "FriendA");
        AssertPacket(projection.RelationPackets[1], FriendBId, GameSocialRelationAction.AddFriendSilently, stateFlag: 0, "FriendB");
        AssertPacket(projection.RelationPackets[2], EnemyAId, GameSocialRelationAction.AddEnemy, stateFlag: 0, "EnemyA");
        AssertPacket(projection.RelationPackets[3], EnemyBId, GameSocialRelationAction.AddEnemy, stateFlag: 1, "EnemyB");
    }

    [Fact]
    public void Create_FriendAndEnemyForSameCounterpart_EmitsBothRelations()
    {
        CharacterSocialRelationSet relationSet = new(CharacterId,
        [
            CreateRelation(FriendAId, SocialRelationKind.Enemy, "Shared"),
            CreateRelation(FriendAId, SocialRelationKind.Friend, "Shared"),
        ]);
        FakePresenceReader presence = new(FriendAId);

        ExistingCharacterFriendListWireProjection projection = ExistingCharacterFriendListWireProjection.Create(relationSet, presence);

        Assert.Equal(2, projection.RelationPackets.Count);
        AssertPacket(projection.RelationPackets[0], FriendAId, GameSocialRelationAction.AddFriendSilently, stateFlag: 1, "Shared");
        AssertPacket(projection.RelationPackets[1], FriendAId, GameSocialRelationAction.AddEnemy, stateFlag: 1, "Shared");
        Assert.Equal([FriendAId, FriendAId], presence.RequestedCharacterIds);
    }

    private static CharacterSocialRelation CreateRelation(uint counterpartCharacterId, SocialRelationKind kind, string name)
    {
        return new CharacterSocialRelation(SocialRelation.Create(CharacterId, counterpartCharacterId, kind), name);
    }

    private static void AssertPacket(GameSocialRelationPacket1019 packet, uint entityId, GameSocialRelationAction action, byte stateFlag, string name)
    {
        Assert.Equal(entityId, packet.EntityId);
        Assert.Equal(action, packet.Action);
        Assert.Equal(stateFlag, packet.StateFlag);
        Assert.Equal(0u, packet.PeerageRank);
        Assert.Equal(0u, packet.PeerageSex);
        Assert.Equal(name, packet.Name);
    }

    private sealed class FakePresenceReader(params uint[] onlineCharacterIds) : ICharacterPresenceReader
    {
        private readonly HashSet<uint> _onlineCharacterIds = [.. onlineCharacterIds];

        public List<uint> RequestedCharacterIds { get; } = [];

        public bool IsOnline(uint characterId)
        {
            RequestedCharacterIds.Add(characterId);
            return _onlineCharacterIds.Contains(characterId);
        }
    }
}
