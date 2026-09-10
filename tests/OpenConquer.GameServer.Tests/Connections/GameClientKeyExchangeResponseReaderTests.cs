using System.Buffers;
using System.IO.Pipelines;
using OpenConquer.GameServer.Connections;

namespace OpenConquer.GameServer.Tests.Connections;

public sealed class GameClientKeyExchangeResponseReaderTests
{
    private static readonly byte[] s_validResponse = Convert.FromHexString("85C2B3D1018C8DAF7C1DC6117ADFB29C937F119D084E59F2D93A05A594B292581E4D56B7AF1318D612");
    private static readonly byte[] s_invalidSignatureResponse = Convert.FromHexString("85C2B3D1018C8DAF7C1DC6117ADFB29C937F119D084E59F2D93A05A594B292581E576880951B11D123");

    [Fact]
    public async Task ReadAsync_ParsesResponseAcrossFragmentedPipeWrites()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);
        Task<string?> readTask = reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        await pipe.Writer.WriteAsync(s_validResponse.AsMemory(0, 5), TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);

        await pipe.Writer.WriteAsync(s_validResponse.AsMemory(5, 6), TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);

        await pipe.Writer.WriteAsync(s_validResponse.AsMemory(11), TestContext.Current.CancellationToken);

        Assert.Equal("05", await readTask);

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_PreservesCoalescedSecuredBytes()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);
        byte[] securedBytes = Enumerable.Range(1, 36).Select(static value => checked((byte)value)).ToArray();
        byte[] coalesced = [.. s_validResponse, .. securedBytes];

        await pipe.Writer.WriteAsync(coalesced, TestContext.Current.CancellationToken);

        Assert.Equal("05", await reader.ReadAsync(TestContext.Current.CancellationToken));

        ReadResult remaining = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal(securedBytes, remaining.Buffer.ToArray());
        }
        finally
        {
            pipe.Reader.AdvanceTo(remaining.Buffer.End);
        }

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_CleanCompletedInputReturnsNull()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);

        await pipe.Writer.CompleteAsync();

        Assert.Null(await reader.ReadAsync(TestContext.Current.CancellationToken));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_TruncatedCompletedInputThrowsEndOfStreamException()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);

        await pipe.Writer.WriteAsync(s_validResponse.AsMemory(0, s_validResponse.Length - 1), TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_InvalidResponseThrowsInvalidDataExceptionAndBecomesTerminal()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);

        await pipe.Writer.WriteAsync(s_invalidSignatureResponse, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_CanceledPipelineReadCanBeRetried()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);
        Task<string?> canceledRead = reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.False(canceledRead.IsCompleted);

        pipe.Reader.CancelPendingRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead);

        await pipe.Writer.WriteAsync(s_validResponse, TestContext.Current.CancellationToken);

        Assert.Equal("05", await reader.ReadAsync(TestContext.Current.CancellationToken));

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ReadAsync_RejectsConcurrentReads()
    {
        Pipe pipe = new();
        GameClientKeyExchangeResponseReader reader = new(pipe.Reader);
        Task<string?> firstRead = reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.False(firstRead.IsCompleted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        pipe.Reader.CancelPendingRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }
}
