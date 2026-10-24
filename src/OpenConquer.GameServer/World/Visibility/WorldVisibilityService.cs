using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.World.Chat;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.World.Visibility;

/// <summary>
/// Sends player spawns, walk relays and removals between players who can see each other on a map. It never
/// does I/O while holding a map's lock; every recipient has its own fixed-size queue.
/// </summary>
internal sealed partial class WorldVisibilityService(ILogger<WorldVisibilityService> logger)
{
    private readonly ILogger<WorldVisibilityService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<uint, MapState> _maps = new();
    private readonly ConcurrentDictionary<string, WorldSession> _onlineByName = new(StringComparer.OrdinalIgnoreCase);

    internal void ReportPeerWriteFailure(uint characterId, Exception exception) =>
        LogPeerWriteFailed(_logger, characterId, exception);

    [LoggerMessage(EventId = 6650, Level = LogLevel.Warning,
        Message = "Peer-visibility delivery failed for character {CharacterId}; closing that recipient's session.")]
    private static partial void LogPeerWriteFailed(ILogger logger, uint characterId, Exception exception);

    public void Enter(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfInactive();
        uint id = session.Ownership.CharacterId;
        GamePlayerSpawnSnapshot spawn = CreateSpawn(session, session.Profile.Location.X, session.Profile.Location.Y, 0);
        MapState map = _maps.GetOrAdd(session.Ownership.MapId, static _ => new MapState());
        lock (map.Gate)
        {
            if (map.Sessions.ContainsKey(id))
            {
                throw new InvalidOperationException("World visibility already contains this character.");
            }
            // Add before links are created so neither side can observe a non-existent peer.
            map.Sessions.Add(id, session);
            map.Spawns.Add(id, spawn);
            if (!_onlineByName.TryAdd(session.Profile.Identity.Name, session))
            {
                map.Sessions.Remove(id);
                map.Spawns.Remove(id);
                throw new InvalidOperationException("World visibility already contains this character name.");
            }
            VisibilityChange transition;
            try
            {
                transition = map.Index.Enter(id, session.Profile.Location.X, session.Profile.Location.Y);
            }
            catch
            {
                map.Sessions.Remove(id);
                map.Spawns.Remove(id);
                RemoveOnlineName(session);
                throw;
            }
            List<IPacket> initialVisibility = new(transition.Entered.Length * 2);
            foreach (uint otherId in transition.Entered)
            {
                map.Sessions[otherId].QueueVisibilityPacket(new GamePlayerSpawnPacket(spawn));
                if (map.RoleWords.TryGetValue(id, out GameChatPacket? entrantWord))
                {
                    map.Sessions[otherId].QueueVisibilityPacket(entrantWord);
                }
                initialVisibility.Add(new GamePlayerSpawnPacket(map.Spawns[otherId]));
                if (map.RoleWords.TryGetValue(otherId, out GameChatPacket? existingWord))
                {
                    initialVisibility.Add(existingWord);
                }
            }
            if (initialVisibility.Count != 0)
            {
                session.QueueVisibilitySnapshot([.. initialVisibility]);
            }
        }
    }

    /// <summary>
    /// Switches visibility between maps after durable/world ownership was committed.
    /// The online-name identity remains attached to the same session throughout.
    /// </summary>
    internal void Relocate(WorldSession session, WorldCharacterOwnership previous, ushort x, ushort y,
        uint destinationMapDataId, uint timestamp, GameMapInfoPacket mapInfo)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mapInfo);
        WorldCharacterOwnership current = session.Ownership;
        if (previous.CharacterId != current.CharacterId || previous.MapId == current.MapId ||
            !_maps.TryGetValue(previous.MapId, out MapState? source))
        {
            throw new InvalidOperationException("Portal visibility source is not registered.");
        }

        MapState destination = _maps.GetOrAdd(current.MapId, static _ => new MapState());
        MapState first = previous.MapId < current.MapId ? source : destination;
        MapState second = previous.MapId < current.MapId ? destination : source;

        lock (first.Gate)
        {
            lock (second.Gate)
            {
                uint characterId = current.CharacterId;
                if (!source.Sessions.TryGetValue(characterId, out WorldSession? existing) ||
                    !ReferenceEquals(existing, session) || destination.Sessions.ContainsKey(characterId) ||
                    !_onlineByName.TryGetValue(session.Profile.Identity.Name, out WorldSession? named) ||
                    !ReferenceEquals(named, session))
                {
                    throw new InvalidOperationException("Portal visibility cannot rebind a stale or duplicated session.");
                }

                byte direction = source.Spawns[characterId].Direction;
                uint[] departed = source.Index.Leave(characterId);
                source.Sessions.Remove(characterId);
                source.Spawns.Remove(characterId);
                source.RoleWords.Remove(characterId);
                GameActionPacket removal = new(characterId, parameterPair: 0, actionParameter: 0,
                    timestamp: unchecked((uint)Environment.TickCount64), action: 135, direction: 0,
                    positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);
                foreach (uint observerId in departed)
                {
                    source.Sessions[observerId].QueueVisibilityPacket(removal);
                }

                // Use the same authoritative facing for the travelling client and destination peers.
                GameActionPacket arrival = GameActionPacket.CreatePortalArrival(
                    characterId, destinationMapDataId, x, y, direction, timestamp);
                // Both map locks prevent a late old-map fanout from entering the
                // sender mailbox behind 86/1110, and destination spawns from entering ahead.
                session.QueuePortalArrival(arrival, mapInfo);
                GamePlayerSpawnSnapshot spawn = CreateSpawn(session, x, y, direction);
                destination.Sessions.Add(characterId, session);
                destination.Spawns.Add(characterId, spawn);
                VisibilityChange change = destination.Index.Enter(characterId, x, y);
                List<IPacket> initialVisibility = new(change.Entered.Length * 2);
                foreach (uint observerId in change.Entered)
                {
                    destination.Sessions[observerId].QueueVisibilityPacket(new GamePlayerSpawnPacket(spawn));
                    initialVisibility.Add(new GamePlayerSpawnPacket(destination.Spawns[observerId]));
                    if (destination.RoleWords.TryGetValue(observerId, out GameChatPacket? word))
                    {
                        initialVisibility.Add(word);
                    }
                }
                if (initialVisibility.Count != 0)
                {
                    session.QueueVisibilitySnapshot([.. initialVisibility]);
                }
            }
        }
    }

    public void Moved(WorldSession session, WorldCharacterSnapshot committed, GameWalkPacket packet) =>
        PublishMovement(session, committed, packet, packet.EntityId, packet.FacingDirection);

    public void Jumped(WorldSession session, WorldCharacterSnapshot committed, GameActionPacket packet) =>
        PublishMovement(session, committed, packet, packet.EntityId, (byte)(packet.Direction % 8));

    /// <summary>Publishes a committed ghost transition and updates late-join spawn state.</summary>
    internal void Ghosted(WorldSession session, WorldCharacterSnapshot ghost, uint ghostLook)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsStopping || ghost.Ownership != session.Ownership || !ghost.IsGhost ||
            !_maps.TryGetValue(session.Ownership.MapId, out MapState? map))
        {
            return;
        }
        lock (map.Gate)
        {
            uint id = session.Ownership.CharacterId;
            if (!map.Sessions.TryGetValue(id, out WorldSession? registered) || !ReferenceEquals(registered, session))
            {
                return;
            }

            GamePlayerSpawnSnapshot prior = map.Spawns[id];
            map.Spawns[id] = prior with
            {
                Appearance = ghostLook,
                StatusMaskLow = ghost.Status.Low,
                StatusMaskHigh = ghost.Status.High,
                Action = GameAvatarAction.StandingPose,
            };
            GameUserAttributePacket status = new(id, GameUserAttributePacket.PrimaryStatusAttribute,
                ghost.Status.Low, ghost.Status.High);
            GameUserAttributePacket appearance = new(id, GameUserAttributePacket.LookAttribute, ghostLook);
            foreach (uint observerId in map.Index.Observers(id))
            {
                WorldSession observer = map.Sessions[observerId];
                observer.QueueVisibilityPacket(status);
                observer.QueueVisibilityPacket(appearance);
            }
        }
    }

    private void PublishMovement(WorldSession session, WorldCharacterSnapshot committed,
        IPacket packet, uint entityId, byte facing)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsStopping || committed.Ownership != session.Ownership || entityId != session.Ownership.CharacterId
            || !_maps.TryGetValue(session.Ownership.MapId, out MapState? map))
        {
            return;
        }
        lock (map.Gate)
        {
            uint id = session.Ownership.CharacterId;
            if (!map.Sessions.TryGetValue(id, out WorldSession? registered) || !ReferenceEquals(registered, session))
            {
                return;
            }
            GamePlayerSpawnSnapshot spawn = map.Spawns[id] with
            {
                X = committed.X,
                Y = committed.Y,
                // Spawn 10014 exposes an eight-direction facing even when
                // movement 10005 uses the 24-entry displacement table.
                Direction = facing,
                Action = GameAvatarAction.StandingPose,
            };
            VisibilityChange change = map.Index.Move(id, committed.X, committed.Y);
            map.Spawns[id] = spawn;
            foreach (uint observerId in change.Retained)
            {
                map.Sessions[observerId].QueueVisibilityPacket(packet);
            }
            foreach (uint observerId in change.Departed)
            {
                map.Sessions[observerId].QueueVisibilityPacket(packet);
            }
            foreach (uint observerId in change.Entered)
            {
                WorldSession observer = map.Sessions[observerId];
                observer.QueueVisibilityPacket(new GamePlayerSpawnPacket(spawn));
                if (map.RoleWords.TryGetValue(id, out GameChatPacket? movingWord))
                {
                    observer.QueueVisibilityPacket(movingWord);
                }
                session.QueueVisibilityPacket(new GamePlayerSpawnPacket(map.Spawns[observerId]));
                if (map.RoleWords.TryGetValue(observerId, out GameChatPacket? observerWord))
                {
                    session.QueueVisibilityPacket(observerWord);
                }
            }
            // At radius 18 the client continuously culls departed characters itself.
        }
    }

    /// <summary>
    /// Publishes an authenticated, nonpositional 5517 avatar action to current observers.
    /// The spawn cache is updated under the same lock used for membership transitions.
    /// </summary>
    public void AvatarAction(WorldSession session, GameAvatarAction request, GameActionPacket acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (session.IsStopping || request.EntityId != session.Ownership.CharacterId ||
            acknowledgement.EntityId != request.EntityId || acknowledgement.Action != request.Kind ||
            !_maps.TryGetValue(session.Ownership.MapId, out MapState? map))
        {
            throw new InvalidDataException("Avatar action has no authenticated active visibility owner.");
        }

        lock (map.Gate)
        {
            uint id = session.Ownership.CharacterId;
            if (!map.Sessions.TryGetValue(id, out WorldSession? current) || !ReferenceEquals(current, session))
            {
                throw new InvalidDataException("Avatar action cannot mutate another world session's visibility state.");
            }

            GamePlayerSpawnSnapshot spawn = map.Spawns[id];
            map.Spawns[id] = request.Kind == GameAvatarAction.DirectionAction
                ? spawn with { Direction = request.Direction }
                : spawn with { Action = request.Pose, Direction = request.Direction };

            foreach (uint viewerId in map.Index.Observers(id))
            {
                map.Sessions[viewerId].QueueVisibilityPacket(acknowledgement);
            }
        }
    }

    /// <summary>
    /// Publishes authenticated local chat, character whispers and transient role words.
    /// Direct recipients are verified against their current session registration.
    /// No network I/O takes place under a map lock.
    /// </summary>
    public GameChatPacket? PublishChat(WorldSession sender, GameInboundChat request)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (!request.IsSupported || sender.IsStopping ||
            !_maps.TryGetValue(sender.Ownership.MapId, out MapState? map))
        {
            throw new InvalidDataException("Chat has no supported channel or active world owner.");
        }

        uint senderId = sender.Ownership.CharacterId;
        if (request.IsWhisper)
        {
            // Do not hold the sender's map lock while looking up or publishing
            // to a recipient in another map; lock ordering must stay acyclic.
            lock (map.Gate)
            {
                EnsureCurrentSession(map, sender);
            }

            if (!_onlineByName.TryGetValue(request.Recipient, out WorldSession? target) || target.IsStopping ||
                !_maps.TryGetValue(target.Ownership.MapId, out MapState? targetMap))
            {
                return null;
            }

            lock (targetMap.Gate)
            {
                if (!targetMap.Sessions.TryGetValue(target.Ownership.CharacterId, out WorldSession? current) ||
                    !ReferenceEquals(current, target) || target.IsStopping)
                {
                    return null;
                }

                GameChatPacket packet = WorldChatPacketFactory.Create(senderId, sender.Profile.Identity.Name,
                    target.Profile.Identity.Name, request);
                if (sender.IsStopping)
                {
                    return null;
                }
                if (!ReferenceEquals(sender, target))
                {
                    target.QueueVisibilityPacket(packet);
                }
                return packet;
            }
        }

        lock (map.Gate)
        {
            EnsureCurrentSession(map, sender);
            GameChatPacket packet = WorldChatPacketFactory.Create(senderId, sender.Profile.Identity.Name,
                GameInboundChat.BroadcastRecipient, request);
            if (request.IsRoleWord)
            {
                map.RoleWords[senderId] = packet;
            }

            foreach (uint viewerId in map.Index.Observers(senderId))
            {
                map.Sessions[viewerId].QueueVisibilityPacket(packet);
            }
            return packet;
        }
    }

    private static void EnsureCurrentSession(MapState map, WorldSession session)
    {
        if (!map.Sessions.TryGetValue(session.Ownership.CharacterId, out WorldSession? current) ||
            !ReferenceEquals(current, session) || session.IsStopping)
        {
            throw new InvalidDataException("Chat cannot publish from a stale world session.");
        }
    }

    private void RemoveOnlineName(WorldSession session)
    {
        // Value-sensitive removal ensures a delayed old-session cleanup cannot
        // evict a newer session registered under the same character name.
        ICollection<KeyValuePair<string, WorldSession>> entries = _onlineByName;
        entries.Remove(new KeyValuePair<string, WorldSession>(session.Profile.Identity.Name, session));
    }

    public void Leave(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(session.Ownership.MapId, out MapState? map))
        {
            return;
        }
        lock (map.Gate)
        {
            uint id = session.Ownership.CharacterId;
            if (!map.Sessions.TryGetValue(id, out WorldSession? registered) || !ReferenceEquals(registered, session))
            {
                return;
            }
            uint[] observers = map.Index.Leave(id);
            map.Sessions.Remove(id);
            map.Spawns.Remove(id);
            map.RoleWords.Remove(id);
            RemoveOnlineName(session);
            GameActionPacket removal = new(id, parameterPair: 0, actionParameter: 0,
                timestamp: unchecked((uint)Environment.TickCount64), action: 135, direction: 0,
                positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);
            foreach (uint observerId in observers)
            {
                map.Sessions[observerId].QueueVisibilityPacket(removal);
            }
        }
    }

    public void Query(WorldSession requester, uint subjectId)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (subjectId == 0 || subjectId == requester.Ownership.CharacterId || requester.IsStopping ||
            !_maps.TryGetValue(requester.Ownership.MapId, out MapState? map))
        {
            return;
        }
        lock (map.Gate)
        {
            uint id = requester.Ownership.CharacterId;
            if (map.Sessions.TryGetValue(id, out WorldSession? active) && ReferenceEquals(active, requester)
                && map.Index.IsObserved(id, subjectId) && map.Spawns.TryGetValue(subjectId, out GamePlayerSpawnSnapshot? spawn))
            {
                requester.QueueVisibilityPacket(new GamePlayerSpawnPacket(spawn));
            }
        }
    }

    private static GamePlayerSpawnSnapshot CreateSpawn(WorldSession session, ushort x, ushort y, byte direction)
    {
        var profile = session.Profile;
        uint head = 0, armor = 0, left = 0, right = 0;
        foreach (CharacterItem item in session.Bootstrap.ItemSet.Items)
        {
            if (item.Placement.EquipmentPosition is not { Set: EquipmentSet.Main } position)
            {
                continue;
            }
            switch (position.Slot)
            {
                case EquipmentSlot.Headwear: head = item.ItemTypeId; break;
                case EquipmentSlot.Armor: armor = item.ItemTypeId; break;
                case EquipmentSlot.LeftHand: left = item.ItemTypeId; break;
                case EquipmentSlot.RightHand: right = item.ItemTypeId; break;
            }
        }
        uint syndicateId = session.Bootstrap.SyndicateState.Syndicate?.SyndicateId ?? 0;
        GamePlayerSpawnSnapshot spawn = new(
            profile.Identity.CharacterId, profile.Identity.Name, profile.Appearance.Composite, profile.Appearance.Hair,
            x, y, direction, profile.Vitals.Life, profile.Progression.Level, profile.Progression.RebirthCount,
            profile.Progression.FirstProfession, profile.Progression.PreviousProfession, profile.Progression.Profession,
            profile.TitleId, syndicateId, head, armor, left, right);
        return profile.Vitals.Life == 0
            ? spawn with { StatusMaskLow = 1UL << WorldPrimaryStatusMask.Dead, Action = 0x14B }
            : spawn;
    }

    private sealed class MapState
    {
        public Lock Gate { get; } = new();
        public MapVisibilityIndex Index { get; } = new();
        public Dictionary<uint, WorldSession> Sessions { get; } = [];
        public Dictionary<uint, GamePlayerSpawnSnapshot> Spawns { get; } = [];
        public Dictionary<uint, GameChatPacket> RoleWords { get; } = [];
    }
}
