using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Threading.Channels;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Handshake;

public sealed class LoginAccountRequestReaderTests
{
    private const uint LoginSeed = 0x0012_34AB;

    private const string AccountName = "testacc";

    private const string ServerName = "Conquer";

    private const string Password = "password1";

    private const string EncryptedCredential = "22DB42ACB82F421D" + "AEF13F7A611D5A03" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65";

    private static readonly byte[] s_streamA = BuildStreamA();

    private static readonly byte[] s_streamB = BuildStreamB();

    [Fact]
    public void Constructor_RejectsNullSession()
    {
        Assert.Throws<ArgumentNullException>(() => new LoginAccountRequestReader(null!));
    }

    [Fact]
    public async Task ReadAsync_DecodesVerified1060ThroughConnectionSession()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        connection.QueueReceive(BuildClientFrame(LoginAccountRequestPacket.PacketIdentifier, CreateValidPayload()));

        LoginAccountRequestReader reader = new(session);

        LoginAccountRequestReadResult result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LoginAccountRequestReadStatus.Success, result.Status);

        Assert.Null(result.UnexpectedPacketId);

        using LoginAccountRequest request = Assert.IsType<LoginAccountRequest>(result.Request);

        Assert.Equal(AccountName, request.AccountName);

        Assert.Equal(ServerName, request.ServerName);

        Assert.Equal(Password, ReadPassword(request));
    }

    [Fact]
    public async Task ReadAsync_CleanPeerEndOfStreamReturnsEndOfStream()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        connection.QueueEndOfStream();

        LoginAccountRequestReader reader = new(session);

        LoginAccountRequestReadResult result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LoginAccountRequestReadStatus.EndOfStream, result.Status);

        Assert.Null(result.Request);

        Assert.Null(result.UnexpectedPacketId);
    }

    [Fact]
    public async Task ReadAsync_PreservesUnexpectedPacketIdentifier()
    {
        const ushort protectedAuthenticationPacketId = 1084;

        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        connection.QueueReceive(BuildClientFrame(protectedAuthenticationPacketId, ReadOnlySpan<byte>.Empty));

        LoginAccountRequestReader reader = new(session);

        LoginAccountRequestReadResult result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LoginAccountRequestReadStatus.UnexpectedPacket, result.Status);

        Assert.Equal(protectedAuthenticationPacketId, result.UnexpectedPacketId);

        Assert.Null(result.Request);
    }

    [Fact]
    public async Task ReadAsync_Invalid1060ReturnsInvalidAccountRequest()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        connection.QueueReceive(BuildClientFrame(LoginAccountRequestPacket.PacketIdentifier, ReadOnlySpan<byte>.Empty));

        LoginAccountRequestReader reader = new(session);

        LoginAccountRequestReadResult result = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LoginAccountRequestReadStatus.InvalidAccountRequest, result.Status);

        Assert.Null(result.Request);

        Assert.Null(result.UnexpectedPacketId);
    }

    [Fact]
    public async Task ReadAsync_PropagatesTransportReceiveFailure()
    {
        IOException failure = new("receive failed");

        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        connection.QueueReceiveFailure(failure);

        LoginAccountRequestReader reader = new(session);

        IOException exception = await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ReadAsync_PropagatesCallerCancellation()
    {
        TestTransportConnection connection = new();

        FakeLoginSeedGenerator seedGenerator = new(LoginSeed);

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, TestContext.Current.CancellationToken);

        LoginAccountRequestReader reader = new(session);

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<LoginAccountRequestReadResult> readTask = reader.ReadAsync(cancellation.Token).AsTask();

        Assert.False(readTask.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    private static byte[] CreateValidPayload()
    {
        byte[] payload = new byte[LoginAccountRequestPacket.PayloadLength];

        WriteAscii(AccountName, payload.AsSpan(LoginAccountRequestPacket.AccountNameOffset, LoginAccountRequestPacket.AccountNameLength));

        Convert.FromHexString(EncryptedCredential).CopyTo(payload.AsSpan(LoginAccountRequestPacket.CredentialFieldOffset, LoginAccountRequestPacket.StandardCredentialTransformLength));

        WriteAscii(ServerName, payload.AsSpan(LoginAccountRequestPacket.ServerNameOffset, LoginAccountRequestPacket.ServerNameLength));

        return payload;
    }

    private static byte[] BuildClientFrame(ushort packetId, ReadOnlySpan<byte> payload)
    {
        int frameLength = checked(sizeof(ushort) + sizeof(ushort) + payload.Length);

        byte[] frame = new byte[frameLength];

        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frameLength));

        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(sizeof(ushort)), packetId);

        payload.CopyTo(frame.AsSpan(sizeof(ushort) + sizeof(ushort)));

        EncryptClientOutbound(frame);

        return frame;
    }

    private static void EncryptClientOutbound(Span<byte> frame)
    {
        ushort position = 0;

        for (int index = 0; index < frame.Length; index++)
        {
            byte value = frame[index];

            value ^= s_streamA[(byte)position];

            value ^= s_streamB[(byte)(position >> 8)];

            value = SwapNibbles(value);

            value ^= 0xAB;

            frame[index] = value;

            position = unchecked((ushort)(position + 1));
        }
    }

    private static byte[] BuildStreamA()
    {
        byte[] stream = new byte[byte.MaxValue + 1];

        byte key = 0x9D;

        for (int index = 0; index < stream.Length; index++)
        {
            stream[index] = key;

            byte multiplied = unchecked((byte)(key * 0xFA));

            byte added = unchecked((byte)(multiplied + 0x0F));

            key = unchecked((byte)((added * key) + 0x13));
        }

        return stream;
    }

    private static byte[] BuildStreamB()
    {
        byte[] stream = new byte[byte.MaxValue + 1];

        byte key = 0x62;

        for (int index = 0; index < stream.Length; index++)
        {
            stream[index] = key;

            byte multiplied = unchecked((byte)(key * 0x5C));

            byte subtracted = unchecked((byte)(0x79 - multiplied));

            key = unchecked((byte)((subtracted * key) + 0x6D));
        }

        return stream;
    }

    private static byte SwapNibbles(byte value)
    {
        return (byte)((value >> 4) | (value << 4));
    }

    private static string ReadPassword(LoginAccountRequest request)
    {
        char[] password = new char[request.PasswordLength];

        request.CopyPasswordTo(password);

        return new string(password);
    }

    private static void WriteAscii(string value, Span<byte> destination)
    {
        Encoding.ASCII.GetBytes(value, destination);
    }

    private sealed class FakeLoginSeedGenerator(uint seed) : ILoginSeedGenerator
    {
        public uint GenerateSeed()
        {
            return seed;
        }
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Channel<ReceiveOperation> _receiveOperations = Channel.CreateUnbounded<ReceiveOperation>();

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 40000);

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReceiveOperation operation = await _receiveOperations.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (operation.Failure is not null)
            {
                throw operation.Failure;
            }

            if (operation.IsEndOfStream)
            {
                return 0;
            }

            byte[] bytes = operation.Bytes ?? throw new InvalidOperationException("Receive operation does not contain bytes.");

            if (bytes.Length > buffer.Length)
            {
                throw new InvalidOperationException("Test receive does not fit the supplied transport buffer.");
            }

            bytes.CopyTo(buffer);

            return bytes.Length;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void QueueReceive(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (!_receiveOperations.Writer.TryWrite(new ReceiveOperation(bytes, Failure: null, IsEndOfStream: false)))
            {
                throw new InvalidOperationException("Unable to queue test receive.");
            }
        }

        public void QueueReceiveFailure(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            if (!_receiveOperations.Writer.TryWrite(new ReceiveOperation(Bytes: null, failure, IsEndOfStream: false)))
            {
                throw new InvalidOperationException("Unable to queue test receive failure.");
            }
        }

        public void QueueEndOfStream()
        {
            if (!_receiveOperations.Writer.TryWrite(new ReceiveOperation(Bytes: null, Failure: null, IsEndOfStream: true)))
            {
                throw new InvalidOperationException("Unable to queue test end of stream.");
            }
        }

        private readonly record struct ReceiveOperation(byte[]? Bytes, Exception? Failure, bool IsEndOfStream);
    }
}
