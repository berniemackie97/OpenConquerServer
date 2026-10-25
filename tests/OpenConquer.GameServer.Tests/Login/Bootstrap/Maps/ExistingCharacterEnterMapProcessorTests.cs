using System.Buffers.Binary;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Bootstrap.Maps;
using OpenConquer.GameServer.Tests.Login.Bootstrap.Missions;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Maps;

public sealed class ExistingCharacterEnterMapProcessorTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint ServerTick = 0xA1B2C3D4;

    [Fact]
    public async Task ProcessAsync_ValidRequest_WritesMapInfoWeatherAndAcknowledgement()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.EnterMapAction);

        await CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        byte[][] packets = harness.ReadServerPackets();

        Assert.Equal(3, packets.Length);
        Assert.Equal(GameMapInfoPacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[0]));
        Assert.Equal(GameWeatherUpdatePacket.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[1]));
        Assert.Equal(GameAction.PacketIdentifier, LoginStepHarness.ReadPacketId(packets[2]));

        Assert.Equal(20, packets[0].Length);
        Assert.Equal(LoginStepHarness.MapId, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(4)));
        Assert.Equal(LoginStepHarness.MapDataId, BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(8)));
        Assert.Equal(unchecked((uint)LoginStepHarness.MapFlags), BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(12)));
        Assert.Equal((uint)(LoginStepHarness.MapFlags >> 32), BinaryPrimitives.ReadUInt32LittleEndian(packets[0].AsSpan(16)));

        Assert.Equal(20, packets[1].Length);
        Assert.Equal(GameWeatherUpdatePacket.ClearKind, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[1].AsSpan(16)));

        Assert.Equal(GameAction.FixedPacketLength, packets[2].Length);
        Assert.Equal(CharacterId, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(4)));
        Assert.Equal(LoginStepHarness.MapDataId, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(12)));
        Assert.Equal(ServerTick, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(16)));
        Assert.Equal(GameAction.EnterMapAction, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(20)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(22)));
        Assert.Equal(login.Profile.Location.X, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(24)));
        Assert.Equal(login.Profile.Location.Y, BinaryPrimitives.ReadUInt16LittleEndian(packets[2].AsSpan(26)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packets[2].AsSpan(32)));
        Assert.Equal((byte)0, packets[2][36]);
        Assert.Equal((byte)0, packets[2][37]);

        Assert.True(harness.Presence.IsOnline(CharacterId));
        Assert.Equal(0, harness.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesBeforeRequest_ThrowsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_RetailMissionCatalogBeforeRequest_ConsumesItAndAnswersTheRequest()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueRetailMissionCatalog();
        harness.QueueAction(GameAction.EnterMapAction);

        await CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal([GameMapInfoPacket.PacketIdentifier, GameWeatherUpdatePacket.PacketIdentifier, GameAction.PacketIdentifier],
            harness.ReadServerPackets().Select(LoginStepHarness.ReadPacketId));
        Assert.Equal(0, harness.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_MalformedMissionCatalog_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] frame = RetailMissionCatalog.Build([600]);

        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 4);
        harness.QueuePacket(frame);
        harness.QueueAction(GameAction.EnterMapAction);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains($"before the EnterMap request is malformed: {nameof(GameMissionCatalogReportParseError.UnsupportedSubtype)}", exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PeerClosesDuringMissionCatalog_ThrowsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueuePacket(RetailMissionCatalog.Frames[0]);
        harness.Transport.QueueEndOfStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedPacket_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.EnterMapAction, CharacterId);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 10011);
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(GameActionParseError.InvalidPacketId), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongAction_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.EnterMapAction + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected EnterMap request action", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_WrongCharacter_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        harness.QueueAction(GameAction.EnterMapAction, entityId: CharacterId + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("character ID", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_TrailingStrings_RejectsWithoutWriting()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        byte[] packet = LoginStepHarness.BuildActionPacket(GameAction.EnterMapAction, CharacterId, length: 41, stringCount: 1);

        packet[38] = 2;
        packet[39] = (byte)'A';
        packet[40] = (byte)'B';
        harness.QueuePacket(packet);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().ProcessAsync(login, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without trailing strings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperation_WritesNothing()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor().ProcessAsync(login, cancellation.Token).AsTask());

        Assert.Equal(0, harness.UnreadServerBytes);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringResponseWrite_SendsNoPartialPacketOnceClosed()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        harness.QueueAction(GameAction.EnterMapAction);
        harness.Transport.BlockSends();

        Task processing = CreateProcessor().ProcessAsync(login, cancellation.Token).AsTask();

        await harness.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        await login.DisposeAsync();
        harness.Transport.ReleaseSends();

        Assert.Equal(0, harness.UnreadServerBytes);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    private static ExistingCharacterEnterMapProcessor CreateProcessor() => new(new LoginStepHarness.FixedGameTickSource(ServerTick));
}
