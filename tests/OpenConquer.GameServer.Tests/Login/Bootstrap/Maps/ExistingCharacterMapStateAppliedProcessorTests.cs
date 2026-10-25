using System.Buffers.Binary;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Maps;
using OpenConquer.GameServer.Tests.Login.Bootstrap.Missions;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Maps;

public sealed class ExistingCharacterMapStateAppliedProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidNotification_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction));

        await new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.True(harness.Presence.IsOnline(LoginStepHarness.CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_RetailMissionCatalogBeforeNotification_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueRetailMissionCatalog();
        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction));

        await new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.Equal(0, harness.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_MissionCatalogAfterACompletedCatalog_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        login.MissionCatalog.Accept(new GameMissionCatalogReport(64, 1, 64));
        harness.QueueRetailMissionCatalog();
        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("second mission catalog", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeNotification_ThrowsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TruncatedAction_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction, length: 20));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.TruncatedBody), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.EnterMapAction));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected client-state-applied notification action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Theory]
    [InlineData(39, 0)]
    [InlineData(GameAction.FixedPacketLength, 1)]
    public async Task ProcessAsync_NonFixedActionBody_RejectsWithoutWriting(int length, byte stringCount)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction, length: length, stringCount: stringCount));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("fixed action body", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(12, 4)]
    [InlineData(16, 4)]
    [InlineData(22, 2)]
    [InlineData(24, 2)]
    [InlineData(26, 2)]
    [InlineData(28, 4)]
    [InlineData(32, 4)]
    [InlineData(36, 1)]
    public async Task ProcessAsync_NonZeroRequestField_RejectsWithoutWriting(int offset, int width)
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.ClientStateAppliedAction);

        LoginStepHarness.WriteNonZeroValue(packet, offset, width);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("all non-action fields to be zero", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingNotification_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task processing = new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationWhileAwaitingNotificationAfterMissionCatalog_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // A one-frame catalog fits below the pipe's backpressure threshold, so it can be
        // completely flushed before the processor starts reading it.
        harness.QueuePacket(RetailMissionCatalog.Build(RetailMissionCatalog.MissionIds[..64]));
        await harness.Transport.WaitForQueuedReceivesAsync(TestContext.Current.CancellationToken);

        // All catalog bytes are in the pipe before starting. The processor consumes those buffered
        // frames synchronously and first yields when it asks for the missing notification.

        Task processing = new ExistingCharacterMapStateAppliedProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        Assert.False(processing.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.Equal(0, harness.UnreadServerBytes);
    }
}
