namespace OpenConquer.GameServer.Tests.Connections;

public sealed class FakeGameTransportConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveAsync_PreCanceledReadDoesNotConsumeQueuedOrPendingBytes(bool partialRead)
    {
        await using FakeGameTransportConnection connection = new();
        connection.QueueReceive([1, 2, 3]);
        byte[] buffer = new byte[1];
        if (partialRead)
        {
            Assert.Equal(1, await connection.ReceiveAsync(buffer, TestContext.Current.CancellationToken));
            Assert.Equal(1, buffer[0]);
        }

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ReceiveAsync(buffer, cancellation.Token).AsTask());
        Assert.True(connection.ReceiveCancellationObserved.IsCompletedSuccessfully);

        Assert.Equal(1, await connection.ReceiveAsync(buffer, TestContext.Current.CancellationToken));
        Assert.Equal(partialRead ? 2 : 1, buffer[0]);
    }
}
