using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.World.Chat;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.GameServer.World.Visibility;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.World;

/// <summary>Injected location-store outcome for connected-portal persistence tests.</summary>
public enum PortalPersistenceFault { None, Conflict, CommittedButUnknown, WrongReceipt }

/// <summary>
/// Admits world sessions over the negotiated, encrypted production game transport and records the
/// location store state that portal transfers read and write.
/// </summary>
internal sealed class WorldSessionTestWorld : IAsyncDisposable, ICharacterLocationWriteStore,
    ICharacterLocationObservationStore
{
    public const uint FirstMap = 1002;
    public const uint SecondMap = 1003;
    public const uint FirstCharacter = CharacterIdentityPolicy.FirstPlayerEntityId;

    private readonly WorldMapContent _first;
    private readonly WorldMapContent _second;
    private readonly object _persistedGate = new();
    private readonly Dictionary<(uint AccountId, uint CharacterId), PersistedCharacterLocation> _saved = [];
    private readonly WorldConnectedPortalTransferCoordinator? _portals;
    private readonly bool _enablePortals;
    private readonly CharacterPresenceDirectory _presence = new();
    private readonly List<WorldSessionTestClient> _clients = [];
    private readonly WorldRuntime _runtime;

    public WorldSessionTestWorld(bool portals = false)
    {
        _enablePortals = portals;
        _first = CreateWideMap(FirstMap, 1015, portals ? [new MapTerrainExit(2, 1, 7)] : []);
        _second = portals ? CreateWideMap(SecondMap, 1016) :
            WorldMapTestContent.Create(SecondMap, 1016, 0);
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(_first, _second);
        _runtime = new WorldRuntime(maps, new WorldExecutionOptions(2, 8, 4, portals ? 512 : 16, portals ? 512 : 16));
        if (portals)
        {
            MapTransitionCatalog catalog = new(
                [new MapTransitionEntry(FirstMap, 7, "resolved",
                    new MapTransitionDestination(SecondMap, 1, 1, 1, "L"))], maps);
            _portals = new WorldConnectedPortalTransferCoordinator(_runtime, catalog,
                this, this, Visibility, NullLogger<WorldConnectedPortalTransferCoordinator>.Instance);
        }
        _runtime.Start();
    }

    public WorldRuntime Runtime => _runtime;
    public PortalPersistenceFault PersistenceFault { get; set; }
    public TaskCompletionSource? CommitReached { get; set; }
    public TaskCompletionSource? ReleaseCommit { get; set; }

    public PersistedCharacterLocation Saved(uint accountId, uint characterId)
    {
        lock (_persistedGate)
        {
            return _saved[(accountId, characterId)];
        }
    }

    public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_persistedGate)
        {
            _saved.TryGetValue((accountId, characterId), out PersistedCharacterLocation? value);
            if (PersistenceFault == PortalPersistenceFault.WrongReceipt && value?.LastOperationId is not null)
            {
                value = new PersistedCharacterLocation(value.Location, value.Revision,
                    writerToken: value.WriterToken);
            }
            return ValueTask.FromResult(value);
        }
    }

    public async ValueTask<CharacterLocationWriteResult> TryCommitAsync(CharacterLocationChange change,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_persistedGate)
        {
            if (PersistenceFault == PortalPersistenceFault.Conflict)
            {
                return CharacterLocationWriteResult.Conflict;
            }
            if (!_saved.TryGetValue((change.AccountId, change.CharacterId),
                out PersistedCharacterLocation? saved) ||
                saved.Revision != change.ExpectedRevision ||
                saved.WriterToken != change.WriterToken ||
                saved.Location.MapId != change.Expected.MapId ||
                saved.Location.X != change.Expected.X ||
                saved.Location.Y != change.Expected.Y)
            {
                return CharacterLocationWriteResult.Conflict;
            }

            _saved[(change.AccountId, change.CharacterId)] = new PersistedCharacterLocation(
                change.Destination, change.ExpectedRevision + 1, change.OperationId, change.WriterToken);
        }
        if (CommitReached is TaskCompletionSource reached && ReleaseCommit is TaskCompletionSource release)
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        if (PersistenceFault == PortalPersistenceFault.CommittedButUnknown)
        {
            throw new IOException("Injected loss of the location commit result after durable mutation.");
        }
        return CharacterLocationWriteResult.Committed;
    }

    public WorldVisibilityService Visibility { get; } = new(NullLogger<WorldVisibilityService>.Instance);

    public async Task<WorldSessionTestClient> AddAsync(uint index, string name, uint mapId, ushort x = 0, ushort y = 0, ushort life = 100)
    {
        FakeGameTransportConnection transport = new();
        GameConnectionSession connection = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameClientTestPeer? client = null;
        try
        {
            using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
            client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);
            await connection.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);
            await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);
            transport.QueueReceive(client.EncryptedKeyExchangeResponse);
            string key = Assert.IsType<string>(
                await connection.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
            GameSessionCipher cipher = exchange.Complete(key);
            try
            {
                connection.CompleteHandshake(cipher);
            }
            catch
            {
                cipher.Dispose();
                throw;
            }

            int initialServerBytes = transport.SentBytes.Length;
            uint characterId = FirstCharacter + index;
            uint accountId = 42 + index;
            uint sessionUid = 0x10203040 + index;
            CharacterLoginProfile profile = CreateProfile(characterId, accountId, name, mapId, x, y, life);
            AuthenticatedGameConnection authenticated = new(accountId, name, sessionUid,
                0x6E45, 0x0000665544332211, 5517, connection);
            CharacterLocationWriteAuthority? authority = null;
            if (_enablePortals)
            {
                Guid token = Guid.NewGuid();
                PersistedCharacterLocation persisted = new(new CharacterLocation(mapId, x, y),
                    9, writerToken: token);
                lock (_persistedGate)
                {
                    _saved.Add((accountId, characterId), persisted);
                }
                authority = new CharacterLocationWriteAuthority(accountId, characterId, token, persisted);
            }
            ExistingCharacterGameConnection game = new(authenticated, profile, _presence.Register(characterId),
                TestAccountSessions.CreateLease(accountId, sessionUid), authority);
            WorldMapContent map = mapId == FirstMap ? _first : _second;
            ExistingCharacterBootstrapCompleteConnection bootstrap = new(game, map,
                new CharacterItemSet(characterId, []), new CharacterSocialRelationSet(characterId, []),
                new CharacterWeaponSkillSet(characterId, []), new CharacterMagicSet(characterId, []),
                new CharacterSyndicateState(characterId, membership: null, syndicate: null), 0, 0);

            WorldSession session = await new WorldSessionAdmissionProcessor(_runtime, Visibility,
                portals: _portals).ProcessAsync(bootstrap, TestContext.Current.CancellationToken);
            WorldSessionTestClient owned = new(session, transport, client, initialServerBytes);
            _clients.Add(owned);
            return owned;
        }
        catch
        {
            client?.Dispose();
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (WorldSessionTestClient client in _clients.AsEnumerable().Reverse())
        {
            await client.Session.DisposeAsync();
            client.DisposeClient();
        }
        await _runtime.DisposeAsync();
    }

    private static WorldMapContent CreateWideMap(uint mapId, uint terrainId, MapTerrainExit[]? exits = null)
    {
        const int size = 64;
        MapBaseTerrain terrain = new(terrainId, size, size,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), size * size), exits ?? []);
        return new WorldMapContent(new MapDefinition(mapId, terrainId, 0),
            new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile CreateProfile(uint id, uint accountId, string name, uint mapId, ushort x, ushort y, ushort life)
    {
        CharacterLoginIdentity identity = new(id, accountId, name);
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(life, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId, x, y);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals,
            economy, 0, 0, 0, location);
    }
}

internal sealed class WorldSessionTestClient(WorldSession session, FakeGameTransportConnection transport,
    GameClientTestPeer client, int firstSecuredOffset)
{
    private readonly List<byte> _incomplete = [];
    private readonly List<byte[]> _frames = [];
    private int _consumed = firstSecuredOffset;

    public WorldSession Session { get; } = session;
    public FakeGameTransportConnection Transport { get; } = transport;

    public void DisposeClient() => client.Dispose();

    public IReadOnlyList<byte[]> AvailableFrames()
    {
        byte[] all = Transport.SentBytes;
        if (all.Length > _consumed)
        {
            byte[] decrypted = client.DecryptServerBytes(all.AsSpan(_consumed));
            _consumed = all.Length;
            _incomplete.AddRange(decrypted);
            ExtractFrames();
        }
        return _frames;
    }

    public async Task<IReadOnlyList<byte[]>> WaitForFrameCountAsync(int count)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (AvailableFrames().Count < count)
        {
            await Task.Delay(10, timeout.Token);
        }
        return _frames;
    }

    public async Task ExpectFrameAsync(byte[] expected)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!AvailableFrames().Any(frame => frame.AsSpan().SequenceEqual(expected)))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public async Task ExpectPacketAsync(GameChatPacket packet)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!AvailableFrames().Any(frame => FrameMatches(frame, packet)))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private void ExtractFrames()
    {
        while (_incomplete.Count >= WireFrameHeader.Size)
        {
            int size = _incomplete[0] | _incomplete[1] << 8;
            if (size < WireFrameHeader.Size || size > GameWireProtocol.MaximumPacketLength)
            {
                throw new InvalidDataException("Encrypted test peer received an invalid frame length.");
            }
            int total = checked(size + GameWireProtocol.SignatureLength);
            if (_incomplete.Count < total)
            {
                return;
            }
            byte[] bytes = _incomplete.GetRange(0, total).ToArray();
            if (!bytes.AsSpan(size, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature))
            {
                throw new InvalidDataException("Encrypted test peer received an invalid server signature.");
            }
            _frames.Add(bytes[..size]);
            _incomplete.RemoveRange(0, total);
        }
    }

    private static bool FrameMatches(ReadOnlySpan<byte> frame, GameChatPacket packet)
    {
        byte[] expected = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, expected);
        return frame.SequenceEqual(expected);
    }
}
