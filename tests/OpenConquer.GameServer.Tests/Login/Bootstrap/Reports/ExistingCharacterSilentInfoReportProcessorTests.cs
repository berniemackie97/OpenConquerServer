using System.Buffers.Binary;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Reports;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Reports;

public sealed class ExistingCharacterSilentInfoReportProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1u)]
    [InlineData(0xAABB_CCDDu, 17u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public async Task ProcessAsync_ValidReport_AcknowledgesWithHeroIdentityAndKeepsUntrustedValues(uint checksum, uint version)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        QueueReport(harness, checksum: checksum, version: version);

        await new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal(checksum, login.ClientReportedSilentDataChecksum);
        Assert.Equal(version, login.ClientReportedSilentDataVersion);
        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);

        byte[] acknowledgement = Assert.Single(harness.ReadServerPackets());

        AssertAcknowledgement(acknowledgement, checksum, version);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeReport_ThrowsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_TransportReadFailure_PreservesException()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException failure = new("inbound transport failed");

        harness.Transport.QueueReceiveFailure(failure);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        QueueReport(harness, action: GameAction.GetSyndicateAttributesAction);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_EarlyStatisticsRequest_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        QueueReport(harness, action: GameAction.GetStatisticAction);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacketIdentifier_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 9999);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedReport_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction, length: 37));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_TrailingBytes_AreRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction, length: 41));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_NonZeroStringCount_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction, stringCount: 1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Theory]
    [InlineData(CharacterId)]
    [InlineData(CharacterId + 1)]
    [InlineData(uint.MaxValue)]
    public async Task ProcessAsync_NonZeroEntityId_IsRejected(uint entityId)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        QueueReport(harness, entityId: entityId);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("zero entity ID", exception.Message, StringComparison.Ordinal);
        AssertNothingRecordedOrWritten(harness, login);
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
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.ReportSilentInfoAction, parameterPair: uint.MaxValue, timestamp: uint.MaxValue);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_DoesNotReadOrWrite()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int receiveCount = harness.Transport.ReceiveCallCount;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCount, harness.Transport.ReceiveCallCount);
        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingReport_StopsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        AssertNothingRecordedOrWritten(harness, login);
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationWhileAwaitingReport_DoesNotRemoveReplacement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        Task processing = new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask();

        ICharacterPresenceLease replacement = harness.Presence.Register(CharacterId);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

            Assert.True(harness.Presence.IsOnline(CharacterId));
            Assert.Equal(0, harness.UnreadServerBytes);
        }
        finally
        {
            replacement.Dispose();
        }

        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringAcknowledgementWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        QueueReport(harness, checksum: 0xAABB_CCDD, version: 17);
        harness.Transport.BlockSends();

        Task processing = new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        try
        {
            await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
            await login.DisposeAsync();
        }
        finally
        {
            harness.Transport.ReleaseSends();
        }

        AssertNothingRecordedOrWritten(harness, login);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationDuringAcknowledgement_DoesNotRemoveReplacement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        QueueReport(harness);
        harness.Transport.BlockSends();

        Task processing = new ExistingCharacterSilentInfoReportProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        ICharacterPresenceLease replacement = harness.Presence.Register(CharacterId);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
            await login.DisposeAsync();

            Assert.True(harness.Presence.IsOnline(CharacterId));
        }
        finally
        {
            harness.Transport.ReleaseSends();
            replacement.Dispose();
        }

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static void QueueReport(LoginStepHarness harness, ushort action = GameAction.ReportSilentInfoAction, uint entityId = 0,
        uint checksum = 0, uint version = 1)
    {
        harness.QueuePacket(LoginStepHarness.BuildActionPacket(action, entityId, parameterPair: checksum, timestamp: version));
    }

    private static void AssertAcknowledgement(byte[] packet, uint checksum, uint version)
    {
        Assert.Equal(GameAction.FixedPacketLength, packet.Length);
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packet));

        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));

        Assert.Equal(checksum, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        Assert.Equal(version, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(GameAction.ReportSilentInfoAction, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.All(packet[22..], value => Assert.Equal((byte)0, value));
    }

    private static void AssertNothingRecordedOrWritten(LoginStepHarness harness, ExistingCharacterLoginContext login)
    {
        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Null(login.ClientReportedSilentDataChecksum);
        Assert.Null(login.ClientReportedSilentDataVersion);
    }
}
