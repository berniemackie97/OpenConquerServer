using System.Buffers.Binary;
using System.Net;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterSilentInfoReportProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1u)]
    [InlineData(0xAABB_CCDDu, 17u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public async Task ProcessAsync_ValidReport_AcknowledgesWithHeroIdentityAndPreservesUntrustedValues(uint checksum, uint version)
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport(checksum: checksum, version: version);

        AwaitingStatisticRequestConnection result = await processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken);

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Same(fixture.Map, result.Map);
        Assert.Same(fixture.ItemSet, result.ItemSet);
        Assert.Same(fixture.SocialRelationSet, result.SocialRelationSet);
        Assert.Same(fixture.WeaponSkillSet, result.WeaponSkillSet);
        Assert.Same(fixture.MagicSet, result.MagicSet);
        Assert.Same(fixture.SyndicateState, result.SyndicateState);
        Assert.Equal(checksum, result.ClientReportedSilentDataChecksum);
        Assert.Equal(version, result.ClientReportedSilentDataVersion);
        Assert.True(fixture.Presence.IsOnline(CharacterId));
        Assert.Equal(0, fixture.Transport.DisposeCount);

        byte[] acknowledgement = Assert.Single(ReadPackets(fixture.DecryptResponse()));

        AssertAcknowledgement(acknowledgement, checksum, version);

        ExistingCharacterGameConnection connection = result.TakeConnection();

        Assert.Same(fixture.Profile, connection.Profile);
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingSilentInfoReport.TakeConnection());
        Assert.Throws<InvalidOperationException>(() => result.TakeConnection());

        await result.DisposeAsync();

        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await connection.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeReport_DisposesConnection()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_TransportReadFailure_DisposesConnectionAndPreservesException()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        IOException failure = new("inbound transport failed");

        fixture.Transport.QueueReceiveFailure(failure);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_IsRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport(action: GameAction10010.GetSyndicateAttributesAction);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_EarlyStatisticsRequest_IsRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport(action: 0x84);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacketIdentifier_IsRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        byte[] packet = BuildActionPacket(GameAction10010.ReportSilentInfoAction, 0);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 9999);
        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedReport_IsRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.ReportSilentInfoAction, 0, length: 37));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_TrailingBytes_AreRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        byte[] packet = BuildActionPacket(GameAction10010.ReportSilentInfoAction, 0, length: 41);

        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_NonZeroStringCount_IsRejected()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueuePacket(BuildActionPacket(GameAction10010.ReportSilentInfoAction, 0, stringCount: 1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Theory]
    [InlineData(CharacterId)]
    [InlineData(CharacterId + 1)]
    [InlineData(uint.MaxValue)]
    public async Task ProcessAsync_NonZeroEntityId_IsRejected(uint entityId)
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport(entityId: entityId);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("zero entity ID", exception.Message, StringComparison.Ordinal);
        AssertFailureBeforeWrite(fixture);
    }

    [Theory]
    [InlineData(12, 4)]
    [InlineData(22, 2)]
    [InlineData(24, 2)]
    [InlineData(26, 2)]
    [InlineData(28, 4)]
    [InlineData(32, 4)]
    [InlineData(36, 1)]
    public async Task ProcessAsync_NonZeroReservedFields_AreRejected(int offset, int width)
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        byte[] packet = BuildActionPacket(GameAction10010.ReportSilentInfoAction, 0, parameterPair: uint.MaxValue,
            timestamp: uint.MaxValue);

        WriteNonZeroValue(packet, offset, width);
        fixture.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_NullState_IsRejectedWithoutTakingConnection()
    {
        ExistingCharacterSilentInfoReportProcessor processor = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            processor.ProcessAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadOrWrite()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCount = fixture.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, cancellation.Token).AsTask());

        Assert.Equal(receiveCount, fixture.Transport.ReceiveCallCount);
        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingReport_DisposesConnection()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<AwaitingStatisticRequestConnection> processing = processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationWhileAwaitingReport_DoesNotRemoveReplacement()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        Task<AwaitingStatisticRequestConnection> processing = processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask();

        ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

            Assert.True(fixture.Presence.IsOnline(CharacterId));
            Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
            Assert.Equal(1, fixture.Transport.DisposeCount);
        }
        finally
        {
            replacement.Dispose();
        }

        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringAcknowledgementWrite_DoesNotSendPartialFrame()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.QueueSilentInfoReport(checksum: 0xAABB_CCDD, version: 17);
        fixture.Transport.BlockSends();

        Task<AwaitingStatisticRequestConnection> processing = processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, cancellation.Token).AsTask();

        try
        {
            await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        AssertFailureBeforeWrite(fixture);
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationDuringAcknowledgement_DoesNotRemoveReplacement()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport();
        fixture.Transport.BlockSends();

        Task<AwaitingStatisticRequestConnection> processing = processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        ICharacterPresenceLease replacement = fixture.Presence.Register(CharacterId);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

            Assert.True(fixture.Presence.IsOnline(CharacterId));
            Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
            Assert.Equal(1, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
            replacement.Dispose();
        }

        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuse_IsRejectedWithoutAnotherReadOrWrite()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport();

        AwaitingStatisticRequestConnection result = await processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken);

        int sentLength = fixture.Transport.SentBytes.Length;
        int receiveCount = fixture.Transport.ReceiveCallCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLength, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCount, fixture.Transport.ReceiveCallCount);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuse_AllowsExactlyOneOwner()
    {
        await using SilentInfoFixture fixture = await CreateFixtureAsync();
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport();
        fixture.Transport.BlockSends();

        Task<AwaitingStatisticRequestConnection> first = processor.ProcessAsync(
            fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        AwaitingStatisticRequestConnection result = await first;

        Assert.Same(fixture.Profile, result.Profile);
        Assert.Single(ReadPackets(fixture.DecryptResponse()));
        Assert.True(fixture.Presence.IsOnline(CharacterId));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_ProcessingAndCleanupFailures_AreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using SilentInfoFixture fixture = await CreateFixtureAsync(cleanupFailure);
        ExistingCharacterSilentInfoReportProcessor processor = new();

        fixture.QueueSilentInfoReport(action: GameAction10010.GetMagicSetAction);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(fixture.AwaitingSilentInfoReport, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        AssertFailureBeforeWrite(fixture);
    }

    private static async Task<SilentInfoFixture> CreateFixtureAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000),
            disposeFailure: disposeFailure);

        (GameConnectionSession session, GameClientTestPeer client) = await OpenSecuredSessionAsync(transport);
        ExistingCharacterGameConnection? connection = null;

        try
        {
            AuthenticatedGameConnection authenticatedConnection = new(AccountId, Username, SessionUid, LocaleTag,
                HardwareAddress, ResourceVersion, session);

            CharacterLoginProfile profile = CreateProfile();
            CharacterPresenceDirectory presence = new();
            connection = new ExistingCharacterGameConnection(authenticatedConnection, profile, presence.Register(CharacterId));

            GameMapEntryDefinition map = new(MapId, MapDataId, MapFlags);
            CharacterItemSet itemSet = new(CharacterId, []);
            CharacterSocialRelationSet socialRelationSet = new(CharacterId, []);
            CharacterWeaponSkillSet weaponSkillSet = new(CharacterId, []);
            CharacterMagicSet magicSet = new(CharacterId, []);
            CharacterSyndicateState syndicateState = new(CharacterId, membership: null, syndicate: null);

            AwaitingSilentInfoReportConnection awaiting = new(connection, map, itemSet, socialRelationSet,
                weaponSkillSet, magicSet, syndicateState);

            return new SilentInfoFixture(transport, client, presence, profile, map, itemSet, socialRelationSet,
                weaponSkillSet, magicSet, syndicateState, awaiting, transport.SentBytes.Length);
        }
        catch
        {
            client.Dispose();

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
            }

            throw;
        }
    }

    private static async Task<(GameConnectionSession Session, GameClientTestPeer Client)> OpenSecuredSessionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        try
        {
            await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);
            transport.QueueReceive(client.EncryptedKeyExchangeResponse);

            string clientPublicKeyHex = Assert.IsType<string>(
                await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));

            Assert.Equal(client.PublicKeyHex, clientPublicKeyHex);

            GameSessionCipher? sessionCipher = exchange.Complete(clientPublicKeyHex);

            try
            {
                session.CompleteHandshake(sessionCipher);
                sessionCipher = null;
            }
            finally
            {
                sessionCipher?.Dispose();
            }

            return (session, client);
        }
        catch
        {
            client.Dispose();
            await session.DisposeAsync();
            throw;
        }
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10,
            previousProfession: 20, rebirthCount: 2, preRebirthLevel: 130);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(MapId, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25,
            titleId: 321, enlightenmentPoints: 1234, location);
    }

    private static byte[] BuildActionPacket(ushort action, uint entityId, uint parameterPair = 0, uint actionParameter = 0,
        uint timestamp = 0, ushort direction = 0, ushort positionX = 0, ushort positionY = 0, uint data1 = 0,
        uint data2 = 0, byte flag = 0, int length = GameAction10010.FixedPacketLength, byte stringCount = 0)
    {
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameAction10010.PacketIdentifier);

        if (length < GameAction10010.FixedPacketLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), entityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), parameterPair);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), actionParameter);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), action);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22), direction);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(24), positionX);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26), positionY);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), data1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), data2);
        packet[36] = flag;
        packet[37] = stringCount;

        return packet;
    }

    private static byte[][] ReadPackets(ReadOnlySpan<byte> plaintext)
    {
        List<byte[]> packets = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header));
            Assert.InRange(header.Length, WireFrameHeader.Size, GameWireProtocol.MaximumPacketLength);

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);

            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength)
                .SequenceEqual(GameWireProtocol.ServerSignature));

            packets.Add(plaintext.Slice(offset, header.Length).ToArray());
            offset += wireLength;
        }

        return packets.ToArray();
    }

    private static void AssertAcknowledgement(byte[] packet, uint checksum, uint version)
    {
        Assert.Equal(GameAction10010.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction10010.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));

        // Unlike the native request, the acknowledgement must resolve to the local hero.
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));

        Assert.Equal(checksum, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(version, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction10010.ReportSilentInfoAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.All(packet[22..], value => Assert.Equal((byte)0, value));
    }

    private static void AssertFailureBeforeWrite(SilentInfoFixture fixture)
    {
        Assert.Equal(fixture.ResponseBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(fixture.Presence.IsOnline(CharacterId));
        Assert.Throws<InvalidOperationException>(() => fixture.AwaitingSilentInfoReport.TakeConnection());
    }

    private static void WriteNonZeroValue(Span<byte> packet, int offset, int width)
    {
        switch (width)
        {
            case 1:
                packet[offset] = 1;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(packet[offset..], 1);
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(packet[offset..], 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(width));
        }
    }

    private sealed class SilentInfoFixture(FakeGameTransportConnection transport, GameClientTestPeer client,
        CharacterPresenceDirectory presence, CharacterLoginProfile profile, GameMapEntryDefinition map,
        CharacterItemSet itemSet, CharacterSocialRelationSet socialRelationSet, CharacterWeaponSkillSet weaponSkillSet,
        CharacterMagicSet magicSet, CharacterSyndicateState syndicateState,
        AwaitingSilentInfoReportConnection awaitingSilentInfoReport, int responseBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public CharacterPresenceDirectory Presence { get; } = presence;
        public CharacterLoginProfile Profile { get; } = profile;
        public GameMapEntryDefinition Map { get; } = map;
        public CharacterItemSet ItemSet { get; } = itemSet;
        public CharacterSocialRelationSet SocialRelationSet { get; } = socialRelationSet;
        public CharacterWeaponSkillSet WeaponSkillSet { get; } = weaponSkillSet;
        public CharacterMagicSet MagicSet { get; } = magicSet;
        public CharacterSyndicateState SyndicateState { get; } = syndicateState;
        public AwaitingSilentInfoReportConnection AwaitingSilentInfoReport { get; } = awaitingSilentInfoReport;
        public int ResponseBoundary { get; } = responseBoundary;

        public void QueueSilentInfoReport(ushort action = GameAction10010.ReportSilentInfoAction, uint entityId = 0,
            uint checksum = 0, uint version = 1)
        {
            QueuePacket(BuildActionPacket(action, entityId, parameterPair: checksum, timestamp: version));
        }

        public void QueuePacket(byte[] packet)
        {
            Transport.QueueReceive(Client.EncryptClientFrame(packet));
        }

        public byte[] DecryptResponse() => Client.DecryptServerBytes(Transport.SentBytes[ResponseBoundary..]);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await AwaitingSilentInfoReport.DisposeAsync();
            }
        }
    }
}
