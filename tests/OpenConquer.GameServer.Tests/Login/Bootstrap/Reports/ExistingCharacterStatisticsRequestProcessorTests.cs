using System.Buffers.Binary;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Reports;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Reports;

public sealed class ExistingCharacterStatisticsRequestProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;

    [Theory]
    [InlineData(0u)]
    [InlineData(0x1122_3344u)]
    [InlineData(uint.MaxValue)]
    public async Task ProcessAsync_ValidRequest_SendsNoResponse(uint timestamp)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.GetStatisticAction, timestamp: timestamp);

        await new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Equal(0, harness.Transport.DisposeCount);
        Assert.True(harness.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TransportReadFailure_PreservesException()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        IOException failure = new("statistics request receive failed");

        harness.Transport.QueueReceiveFailure(failure);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedAction_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.ReportSilentInfoAction);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacketId_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 9999);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedRequest_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId, length: 37));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_ExtraBytes_AreRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId, length: 41));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_NonZeroStringCount_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId, stringCount: 1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(CharacterId + 1)]
    [InlineData(uint.MaxValue)]
    public async Task ProcessAsync_WrongCharacterId_IsRejected(uint entityId)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.GetStatisticAction, entityId: entityId);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("does not match the authenticated character", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Theory]
    [InlineData(8, 4)]
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
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.GetStatisticAction, CharacterId, timestamp: uint.MaxValue);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
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
            new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(receiveCount, harness.Transport.ReceiveCallCount);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileWaiting_StopsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PresenceRevocationWhileWaiting_DoesNotRemoveReplacement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        Task processing = new ExistingCharacterStatisticsRequestProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask();

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
}
