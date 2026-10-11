namespace OpenConquer.Protocol.Game.Packets;

public enum GameSocialRelationAction : byte
{
    FriendRequest = 10,
    AddFriend = 11,
    FriendOnline = 12,
    FriendOffline = 13,
    RemoveFriend = 14,
    AddFriendSilently = 15,
    EnemyOnline = 16,
    EnemyOffline = 17,
    RemoveEnemy = 18,
    AddEnemy = 19,
}
