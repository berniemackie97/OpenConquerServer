using OpenConquer.GameServer.Login;
using System.Buffers.Binary;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Domain.Items;
using OpenConquer.Domain.Skills;
using OpenConquer.Domain.Social;
using OpenConquer.Domain.Syndicates;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Items;
using OpenConquer.GameServer.Login.Bootstrap.Maps;
using OpenConquer.GameServer.Login.Bootstrap.Reports;
using OpenConquer.GameServer.Login.Bootstrap.Skills;
using OpenConquer.GameServer.Login.Bootstrap.Social;
using OpenConquer.GameServer.Login.Bootstrap.Syndicates;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Hosting;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Tests.Login.Bootstrap;
using OpenConquer.GameServer.Tests.Login.Bootstrap.Missions;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Hosting;

public sealed partial class GameExistingCharacterLoginProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint FriendId = CharacterId + 1;
    private const uint EnemyId = CharacterId + 2;
    private const uint ServerTick = 0xA1B2_C3D4;
    private const uint SilentDataChecksum = 0x1234_5678;
    private const uint SilentDataVersion = 17;
    private const ushort UnexpectedAction = 0x7FFF;
    private const int ItemsStep = 2;
    private const int FriendsStep = 3;
    private const int WeaponSkillsStep = 4;

    private static readonly DateTimeOffset s_utcNow = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    // Repository loaded by each client request step, in protocol order.
    private static readonly string?[] s_loadsByStep = [null, null, "items", "relations", "weapon skills", "magic", "syndicate", null, null];

    // Every packet the server sends during a full login, pinned so changes to the login steps cannot alter what the client receives.
    // Entry 0 is the bootstrap burst; entry N answers client request step N - 1.
    private static readonly string[][] s_expectedResponses =
    [
        // Bootstrap: 1004 entry accepted, 2078 login history, 2079 server state, 1006 character info.
        [
            "2600EC0300000000350800000000000000000000000000000400000009414E535745525F4F4B",
            "0A001E08000000000000",
            "08001F0800000000",
            "7C00EE0340420F0000007BAF1E00530187D612002909000015CD5B070000000000000000000000000000000000000000405DC60065006600670068006900D2043702E7FF783C0A1400020100000000D204000000000000000000004101A60200000000000000000000000000000003064265726E696500044E6F6E65",
        ],
        // EnterMap: 1110 map info, 1016 weather, acknowledgement.
        [
            "14005604EA030000F70300008877665544332211",
            "1400F80301000000000000000000000000000000",
            "26001A2740420F00F703000000000000D4C3B2A14A0000000000000000000000000000000000",
        ],
        // Map state applied: no response.
        [],
        // Items: 1008 equipped headwear, 1008 inventory item, 1009 main-set equipment, acknowledgement.
        [
            "4400F0030A000000A08601006400640001000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000",
            "4400F00314000000A08601006400640001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000",
            "5800F10300000000000000002E000000000000000000000000000000000000000A00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
            "26001A2740420F000000000000000000020202024B0000000000000000000000000000000000",
        ],
        // Friends: 1019 online friend, 1019 enemy, acknowledgement.
        [
            "2400FB0341420F000F0000000000000000000000416C6C79000000000000000000000000",
            "2400FB0342420F00130000000000000000000000526976616C0000000000000000000000",
            "26001A2740420F000000000000000000030303034C0000000000000000000000000000000000",
        ],
        // Weapon skills: 1025 per skill, acknowledgement.
        [
            "140001049A01000001000000B0040000B0040000",
            "14000104F40100000300000090D0030090D00300",
            "26001A2740420F000000000000000000040404044D0000000000000000000000000000000000",
        ],
        // Magic: 1103, 1104 for the spell with experience, 1103, acknowledgement.
        [
            "0C004F0440420F00E8030100",
            "100050046400000000000000E8030100",
            "0C004F0440420F00DC050400",
            "26001A2740420F000000000000000000050505054E0000000000000000000000000000000000",
        ],
        // Syndicate: 1106 attributes, acknowledgement.
        [
            "5C005204490000008813000040420F0000000000D007000002000000E8030000416C6C790000000000000000000000006300000002000000230000000000007F4C3501A63A3401000000004775696C64416C70686100000000000000",
            "26001A2740420F00000000000000000006060606610000000000000000000000000000000000",
        ],
        // Silent info: acknowledgement echoing the checksum and version.
        [
            "26001A2740420F00785634120000000011000000FB0000000000000000000000000000000000",
        ],
        // Statistics: no response.
        [],
    ];

    [Fact]
    public async Task ProcessAsync_FullLogin_SendsKnownResponsesAndAdmitsCharacter()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        for (int step = 0; step < s_loadsByStep.Length; step++)
        {
            fixture.QueueRequest(step);
        }

        await using WorldSession session = Assert.IsType<WorldSession>(await fixture.ProcessAsync());

        string[] responses = fixture.Harness.ReadServerPackets().Select(Convert.ToHexString).ToArray();

        Assert.Equal(s_expectedResponses.SelectMany(static step => step), responses);
        Assert.Same(fixture.Profile, session.Profile);
        Assert.Same(fixture.Map, session.Map);
        Assert.Equal([10u, 20u], session.Bootstrap.ItemSet.Items.Select(static item => item.ItemId));
        Assert.Same(fixture.Store.Relations, session.Bootstrap.SocialRelationSet);
        Assert.Same(fixture.Store.WeaponSkills, session.Bootstrap.WeaponSkillSet);
        Assert.Same(fixture.Store.Magic, session.Bootstrap.MagicSet);
        Assert.Same(fixture.Store.Syndicate, session.Bootstrap.SyndicateState);
        Assert.Equal(SilentDataChecksum, session.Bootstrap.ClientReportedSilentDataChecksum);
        Assert.Equal(SilentDataVersion, session.Bootstrap.ClientReportedSilentDataVersion);
        Assert.Equal(["items", "relations", "weapon skills", "magic", "syndicate"], fixture.Store.Loads);
        Assert.Equal(s_utcNow, fixture.Store.ItemResolutionTime);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.NotNull(await fixture.Runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Harness.Transport.DisposeCount);

        await session.DisposeAsync();

        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    // Step 1 is where the retail client's catalog arrived on 2026-10-25: after the EnterMap exchange, before the
    // client-state-applied notification. Step 0 is the same catalog when the client handles ANSWER_OK before character info arrives.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ProcessAsync_RetailMissionCatalogBeforeStep_SendsKnownResponsesAndAdmitsCharacter(int catalogStep)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        for (int step = 0; step < s_loadsByStep.Length; step++)
        {
            if (step == catalogStep)
            {
                fixture.Harness.QueueRetailMissionCatalog();
            }

            fixture.QueueRequest(step);
        }

        await using WorldSession session = Assert.IsType<WorldSession>(await fixture.ProcessAsync());

        Assert.Equal(s_expectedResponses.SelectMany(static step => step), fixture.Harness.ReadServerPackets().Select(Convert.ToHexString));
        Assert.Equal(["items", "relations", "weapon skills", "magic", "syndicate"], fixture.Store.Loads);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(0, fixture.Harness.Transport.DisposeCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ProcessAsync_MissionCatalogAfterClientStateApplied_ClosesConnectionWithoutLaterLoads(int catalogStep)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        for (int previous = 0; previous < catalogStep; previous++)
        {
            fixture.QueueRequest(previous);
        }

        fixture.Harness.QueueRetailMissionCatalog();

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(fixture.ProcessAsync);

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), failure.Message, StringComparison.Ordinal);
        Assert.Equal(s_loadsByStep.Take(catalogStep).OfType<string>(), fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        await AssertNotInWorldAsync(fixture);
    }

    [Fact]
    public async Task ProcessAsync_MissionCatalogSplitAroundEnterMap_ClosesConnectionWithoutLoads()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        fixture.Harness.QueuePacket(RetailMissionCatalog.Frames[0]);
        fixture.QueueRequest(0);
        fixture.Harness.QueuePacket(RetailMissionCatalog.Frames[1]);
        fixture.Harness.QueuePacket(RetailMissionCatalog.Frames[2]);
        fixture.QueueRequest(1);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(fixture.ProcessAsync);

        Assert.Contains("second mission catalog", failure.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task AdmittedSession_AccountLoggedInFromAnotherRealm_SendsTheKickNoticeAndReleasesTheAccount()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        WorldSession session = await fixture.LogInAsync();

        Assert.True(fixture.Presence.Revoke(CharacterId));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        byte[] notice = Assert.Single(fixture.Harness.ReadServerPackets());

        Assert.Equal(GameKickoutNoticePacket.PacketIdentifier, LoginStepHarness.ReadPacketId(notice));
        Assert.Equal((uint)GameKickoutReason.OtherLogin, BinaryPrimitives.ReadUInt32LittleEndian(notice.AsSpan(4)));
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.Equal([(LoginStepHarness.AccountId, fixture.Harness.Connection.SessionUid)], fixture.AccountSessions.Left);
    }

    [Fact]
    public async Task AdmittedSession_AccountLoggedInAgainInThisRealm_SendsTheKickNotice()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        WorldSession session = await fixture.LogInAsync();

        using ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        byte[] notice = Assert.Single(fixture.Harness.ReadServerPackets());

        Assert.Equal(GameKickoutNoticePacket.PacketIdentifier, LoginStepHarness.ReadPacketId(notice));
        Assert.True(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task AdmittedSession_OwnDisconnect_ClosesWithoutTheKickNotice()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();
        WorldSession session = await fixture.LogInAsync();

        await session.DisposeAsync();

        Assert.Empty(fixture.Harness.ReadServerPackets());
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Single(fixture.AccountSessions.Left);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ProcessAsync_WrongActionAtStep_ClosesConnectionWithoutLaterLoads(int step)
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        for (int previous = 0; previous < step; previous++)
        {
            fixture.QueueRequest(previous);
        }

        fixture.Harness.QueueAction(UnexpectedAction);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(fixture.ProcessAsync);

        Assert.Contains($"received action {UnexpectedAction}", failure.Message, StringComparison.Ordinal);
        Assert.Equal(s_loadsByStep.Take(step).OfType<string>(), fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        await AssertNotInWorldAsync(fixture);
    }

    [Fact]
    public async Task ProcessAsync_UnavailableMap_ClosesConnectionBeforeAnyResponse()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync(profileMapId: LoginStepHarness.MapId + 1);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(fixture.ProcessAsync);

        Assert.Contains($"unavailable world map {LoginStepHarness.MapId + 1}", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Harness.UnreadServerBytes);
        Assert.Empty(fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevokedBetweenSteps_ClosesConnectionAndKeepsReplacement()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync();

        for (int step = 0; step <= FriendsStep; step++)
        {
            fixture.QueueRequest(step);
        }

        Task<WorldSession?> login = fixture.ProcessAsync();

        await WaitForResponsesThroughAsync(fixture.Harness, FriendsStep);

        using ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);

        Assert.Equal(["items", "relations"], fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevokedDuringRepositoryLoad_CancelsLoadAndKeepsReplacement()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync(blockedLoad: "weapon skills");

        for (int step = 0; step <= WeaponSkillsStep; step++)
        {
            fixture.QueueRequest(step);
        }

        Task<WorldSession?> login = fixture.ProcessAsync();

        await fixture.Store.LoadBlocked.WaitAsync(TestContext.Current.CancellationToken);

        using ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);

        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_AccountPlayingInAnotherRealm_ReturnsNoSessionWithoutRunningTheBootstrap()
    {
        await using LoginFixture fixture = await LoginFixture.CreateAsync(admitAccountSession: false);

        for (int step = 0; step < s_loadsByStep.Length; step++)
        {
            fixture.QueueRequest(step);
        }

        WorldSession? session = await fixture.ProcessAsync();

        Assert.Null(session);
        Assert.Empty(fixture.Store.Loads);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.Single(fixture.AccountSessions.Entered);
    }

    [Fact]
    public async Task ProcessAsync_StepFailureAndCleanupFailure_AreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using LoginFixture fixture = await LoginFixture.CreateAsync(disposeFailure: cleanupFailure);

        for (int step = 0; step < ItemsStep; step++)
        {
            fixture.QueueRequest(step);
        }

        fixture.Harness.QueueAction(UnexpectedAction);

        AggregateException failure = await Assert.ThrowsAsync<GameLoginCleanupException>(fixture.ProcessAsync);

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(failure.InnerExceptions[0]);
        Assert.Same(cleanupFailure, failure.InnerExceptions[1]);
        Assert.Empty(fixture.Store.Loads);
        Assert.Equal(1, fixture.Harness.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    private static async Task WaitForResponsesThroughAsync(LoginStepHarness harness, int step)
    {
        int expectedBytes = s_expectedResponses.Take(step + 2).SelectMany(static packets => packets)
            .Sum(static packet => (packet.Length / 2) + GameWireProtocol.SignatureLength);

        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (harness.UnreadServerBytes >= expectedBytes)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"The server did not send its responses through login step {step}.");
    }

    private static async Task AssertNotInWorldAsync(LoginFixture fixture)
    {
        WorldCharacterOwnership ownership = await fixture.Runtime.AdmitCharacterAsync(fixture.Profile, TestContext.Current.CancellationToken);
        Assert.True(await fixture.Runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    private sealed class LoginFixture : IAsyncDisposable
    {
        private LoginFixture(LoginStepHarness harness, CharacterLoginProfile profile, WorldMapContent map, WorldRuntime runtime,
            FakeCharacterStore store, RecordingAccountSessionStore accountSessions, CharacterLoginHandoffResult handoff)
        {
            Harness = harness;
            Profile = profile;
            Map = map;
            Runtime = runtime;
            Store = store;
            AccountSessions = accountSessions;
            Handoff = handoff;

            LoginStepHarness.FixedTimeProvider time = new(s_utcNow);

            Processor = new GameExistingCharacterLoginProcessor(
                new ExistingCharacterBootstrapProcessor(Presence, TestAccountSessions.Create(accountSessions), WorldMapTestContent.CreateCatalog(map), Metrics),
                new ExistingCharacterEnterMapProcessor(new LoginStepHarness.FixedGameTickSource(ServerTick)),
                new ExistingCharacterMapStateAppliedProcessor(),
                new ExistingCharacterItemSetProcessor(store, time),
                new ExistingCharacterFriendListProcessor(store, Presence),
                new ExistingCharacterWeaponSkillSetProcessor(store),
                new ExistingCharacterMagicSetProcessor(store),
                new ExistingCharacterSyndicateAttributesProcessor(store, time),
                new ExistingCharacterSilentInfoReportProcessor(),
                new ExistingCharacterStatisticsRequestProcessor(),
                new WorldSessionAdmissionProcessor(runtime));
        }

        public LoginStepHarness Harness { get; }
        public CharacterPresenceDirectory Presence => Harness.Presence;
        public CharacterLoginProfile Profile { get; }
        public WorldMapContent Map { get; }
        public WorldRuntime Runtime { get; }
        public FakeCharacterStore Store { get; }
        public RecordingAccountSessionStore AccountSessions { get; }
        public GameLoginMetrics Metrics { get; } = new();
        public CharacterLoginHandoffResult Handoff { get; }
        public GameExistingCharacterLoginProcessor Processor { get; }

        public static async Task<LoginFixture> CreateAsync(Exception? disposeFailure = null, uint profileMapId = LoginStepHarness.MapId,
            string? blockedLoad = null, bool admitAccountSession = true)
        {
            LoginStepHarness harness = await LoginStepHarness.CreateAsync(disposeFailure);
            CharacterLoginProfile profile = LoginStepHarness.CreateProfile(profileMapId, x: 0, y: 0);
            WorldMapContent map = WorldMapTestContent.Create(LoginStepHarness.MapId, LoginStepHarness.MapDataId, LoginStepHarness.MapFlags);
            WorldRuntime runtime = new(WorldMapTestContent.CreateCatalog(map), new WorldExecutionOptions(2, 8, 4, 8, 8));
            runtime.Start();

            CharacterLoginHandoffResult handoff = new(harness.Connection, CharacterLoginResolution.ExistingCharacter(profile));

            return new LoginFixture(harness, profile, map, runtime, new FakeCharacterStore(blockedLoad),
                new RecordingAccountSessionStore { AdmitEntries = admitAccountSession }, handoff);
        }

        public Task<WorldSession?> ProcessAsync() => Processor.ProcessAsync(Handoff, TestContext.Current.CancellationToken).AsTask();

        /// <summary>
        /// Runs the whole login and discards its responses, leaving an admitted session.
        /// </summary>
        public async Task<WorldSession> LogInAsync()
        {
            for (int step = 0; step < s_loadsByStep.Length; step++)
            {
                QueueRequest(step);
            }

            WorldSession session = Assert.IsType<WorldSession>(await ProcessAsync());
            Harness.ReadServerPackets();
            return session;
        }

        public void QueueRequest(int step)
        {
            byte[] request = step switch
            {
                0 => LoginStepHarness.BuildActionPacket(GameAction.EnterMapAction, CharacterId, timestamp: 0x0101_0101),
                1 => LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction),
                2 => LoginStepHarness.BuildActionPacket(GameAction.GetItemSetAction, CharacterId, timestamp: 0x0202_0202),
                3 => LoginStepHarness.BuildActionPacket(GameAction.GetFriendListAction, CharacterId, timestamp: 0x0303_0303),
                4 => LoginStepHarness.BuildActionPacket(GameAction.GetWeaponSkillSetAction, CharacterId, timestamp: 0x0404_0404),
                5 => LoginStepHarness.BuildActionPacket(GameAction.GetMagicSetAction, CharacterId, timestamp: 0x0505_0505),
                6 => LoginStepHarness.BuildActionPacket(GameAction.GetSyndicateAttributesAction, CharacterId, timestamp: 0x0606_0606),
                7 => LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction, parameterPair: SilentDataChecksum, timestamp: SilentDataVersion),
                8 => LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId, timestamp: 0x0909_0909),
                _ => throw new ArgumentOutOfRangeException(nameof(step)),
            };

            Harness.QueuePacket(request);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await Runtime.DisposeAsync();
            Metrics.Dispose();
        }
    }

    private sealed class FakeCharacterStore(string? blockedLoad) : ICharacterItemSetResolver, ICharacterSocialRelationSetRepository,
        ICharacterWeaponSkillSetRepository, ICharacterMagicSetRepository, ICharacterSyndicateStateRepository
    {
        private readonly List<string> _loads = [];
        private readonly TaskCompletionSource _loadBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CharacterItemSet Items { get; } = new(CharacterId,
        [
            CreateItem(20, ItemPlacement.CreateInventory()),
            CreateItem(10, ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.Headwear))),
        ]);

        public CharacterSocialRelationSet Relations { get; } = new(CharacterId,
        [
            new CharacterSocialRelation(SocialRelation.Create(CharacterId, EnemyId, SocialRelationKind.Enemy), "Rival"),
            new CharacterSocialRelation(SocialRelation.Create(CharacterId, FriendId, SocialRelationKind.Friend), "Ally"),
        ]);

        public CharacterWeaponSkillSet WeaponSkills { get; } = new(CharacterId,
        [
            WeaponSkill.Create(CharacterId, 410, 1, 1_200),
            WeaponSkill.Create(CharacterId, 500, 3, 250_000),
        ]);

        public CharacterMagicSet Magic { get; } = new(CharacterId,
        [
            CharacterMagic.Create(CharacterId, 1000, 1, 100),
            CharacterMagic.Create(CharacterId, 1500, 4, 0),
        ]);

        public CharacterSyndicateState Syndicate { get; } = new(CharacterId,
            CharacterSyndicateMembership.Create(CharacterId, 73, 1000, 5_000,
                positionExpirationUnixSeconds: 1_801_958_400, joinDateUnixSeconds: 1_577_923_200),
            new Syndicate(73, "GuildAlpha", FriendId, "Ally", 1_000_000, 2_000, 2, 99, 35, 2));

        public DateTimeOffset? ItemResolutionTime { get; private set; }
        public Task LoadBlocked => _loadBlocked.Task;

        public string[] Loads
        {
            get
            {
                lock (_loads)
                {
                    return _loads.ToArray();
                }
            }
        }

        public ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            ItemResolutionTime = utcNow;
            return LoadAsync("items", Items, cancellationToken);
        }

        ValueTask<CharacterSocialRelationSet> ICharacterSocialRelationSetRepository.LoadAsync(uint characterId, CancellationToken cancellationToken) =>
            LoadAsync("relations", Relations, cancellationToken);

        ValueTask<CharacterWeaponSkillSet> ICharacterWeaponSkillSetRepository.LoadAsync(uint characterId, CancellationToken cancellationToken) =>
            LoadAsync("weapon skills", WeaponSkills, cancellationToken);

        ValueTask<CharacterMagicSet> ICharacterMagicSetRepository.LoadAsync(uint characterId, CancellationToken cancellationToken) =>
            LoadAsync("magic", Magic, cancellationToken);

        ValueTask<CharacterSyndicateState> ICharacterSyndicateStateRepository.LoadAsync(uint characterId, CancellationToken cancellationToken) =>
            LoadAsync("syndicate", Syndicate, cancellationToken);

        private async ValueTask<T> LoadAsync<T>(string load, T result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_loads)
            {
                _loads.Add(load);
            }

            if (load == blockedLoad)
            {
                _loadBlocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return result;
        }

        private static CharacterItem CreateItem(uint itemId, ItemPlacement placement)
        {
            return new CharacterItem(itemId, CharacterId, 100_000, placement, 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                false, 0, null, 0, 0, 0, 1, ItemLifetime.CreatePermanent());
        }
    }
}
