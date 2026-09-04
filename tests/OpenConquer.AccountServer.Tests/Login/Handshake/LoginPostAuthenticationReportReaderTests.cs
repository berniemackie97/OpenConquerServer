using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Threading.Channels;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Handshake;

public sealed class LoginPostAuthenticationReportReaderTests
{
    private const uint LoginSeed = 0x0012_34AB;
    private const uint SessionUid = 0x1122_3344;

    private static readonly byte[] s_streamA = BuildStreamA();
    private static readonly byte[] s_streamB = BuildStreamB();

    [Fact]
    public void Constructor_RejectsNullSession()
    {
        Assert.Throws<ArgumentNullException>(() => new LoginPostAuthenticationReportReader(null!));
    }

    [Fact]
    public async Task ReadAsync_RejectsZeroExpectedSessionUid()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        LoginPostAuthenticationReportReader reader = new(session);

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadAsync(expectedSessionUid: 0, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("expectedSessionUid", exception.ParamName);
    }

    [Fact]
    public async Task ReadAsync_DecodesVerifiedReportsAfterCredentialFrameThroughConnectionSession()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        byte[] macPayload = BuildMacAddressPayload(SessionUid, "001122AABBCC");

        macPayload.AsSpan(LoginAccountMacAddressReportPacket.TrailingBytesOffset, LoginAccountMacAddressReportPacket.TrailingBytesLength).Fill(0xA5);

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountRequestPacket.PacketIdentifier,
                    new byte[LoginAccountRequestPacket.PayloadLength]
                ),
                (LoginAccountMacAddressReportPacket.PacketIdentifier, macPayload),
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    BuildResourceVersionPayload(
                        SessionUid,
                        resourceVersion: 5517,
                        resourceName: "res.dat"
                    )
                )
            )
        );

        LoginInboundFrame? credentialInboundFrame = await session.ReadAsync(TestContext.Current.CancellationToken);

        using (LoginInboundFrame credentialFrame = Assert.IsType<LoginInboundFrame>(credentialInboundFrame))
        {
            Assert.Equal(LoginAccountRequestPacket.PacketIdentifier, credentialFrame.PacketId);
            Assert.Equal(LoginAccountRequestPacket.PayloadLength, credentialFrame.Payload.Length);
        }

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(SessionUid, TestContext.Current.CancellationToken);

        Assert.Equal(LoginPostAuthenticationReportReadStatus.Success, result.Status);

        Assert.Null(result.FailurePhase);
        Assert.Null(result.UnexpectedPacketId);

        LoginPostAuthenticationReports reports = Assert.IsType<LoginPostAuthenticationReports>(result.Reports);

        Assert.Equal("001122AABBCC", reports.MacAddress);

        Assert.Equal(5517, reports.ResourceVersion);
    }

    [Fact]
    public async Task ReadAsync_AcceptsNativeEmptyMacAddress()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, string.Empty)
                ),
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    BuildResourceVersionPayload(
                        SessionUid,
                        resourceVersion: 0,
                        resourceName: "res.dat"
                    )
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(SessionUid, TestContext.Current.CancellationToken);

        Assert.Equal(LoginPostAuthenticationReportReadStatus.Success, result.Status);
        Assert.Equal(string.Empty, Assert.IsType<LoginPostAuthenticationReports>(result.Reports).MacAddress);
    }

    [Fact]
    public async Task ReadAsync_EndOfStreamBeforeMacAddressReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        connection.QueueEndOfStream();

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(SessionUid, TestContext.Current.CancellationToken);

        AssertFailure(result, LoginPostAuthenticationReportReadStatus.EndOfStream, LoginPostAuthenticationReportPhase.MacAddressReport);
    }

    [Fact]
    public async Task ReadAsync_UnexpectedPacketBeforeMacAddressReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    BuildResourceVersionPayload(SessionUid, 5517, "res.dat")
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.UnexpectedPacket,
            LoginPostAuthenticationReportPhase.MacAddressReport
        );

        Assert.Equal(
            LoginAccountResourceVersionReportPacket.PacketIdentifier,
            result.UnexpectedPacketId
        );
    }

    [Fact]
    public async Task ReadAsync_InvalidMacAddressReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    new byte[LoginAccountMacAddressReportPacket.PayloadLength - 1]
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.InvalidReport,
            LoginPostAuthenticationReportPhase.MacAddressReport
        );
    }

    [Fact]
    public async Task ReadAsync_MacAddressReportSessionMismatchIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid + 1, "001122AABBCC")
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.SessionMismatch,
            LoginPostAuthenticationReportPhase.MacAddressReport
        );
    }

    [Theory]
    [InlineData("001122aabbcc")]
    [InlineData("00:11:22:AA:BB:CC")]
    [InlineData("001122AABBCG")]
    [InlineData("001122AABB")]
    public async Task ReadAsync_InvalidMacAddressIsRejected(string macAddress)
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, macAddress)
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.InvalidMacAddress,
            LoginPostAuthenticationReportPhase.MacAddressReport
        );
    }

    [Fact]
    public async Task ReadAsync_EndOfStreamBeforeResourceVersionReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                )
            )
        );

        connection.QueueEndOfStream();

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.EndOfStream,
            LoginPostAuthenticationReportPhase.ResourceVersionReport
        );
    }

    [Fact]
    public async Task ReadAsync_UnexpectedPacketBeforeResourceVersionReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                ),
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.UnexpectedPacket,
            LoginPostAuthenticationReportPhase.ResourceVersionReport
        );

        Assert.Equal(
            LoginAccountMacAddressReportPacket.PacketIdentifier,
            result.UnexpectedPacketId
        );
    }

    [Fact]
    public async Task ReadAsync_InvalidResourceVersionReportIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                ),
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    new byte[LoginAccountResourceVersionReportPacket.PayloadLength - 1]
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.InvalidReport,
            LoginPostAuthenticationReportPhase.ResourceVersionReport
        );
    }

    [Fact]
    public async Task ReadAsync_ResourceVersionReportSessionMismatchIsClassified()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                ),
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    BuildResourceVersionPayload(SessionUid + 1, 5517, "res.dat")
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.SessionMismatch,
            LoginPostAuthenticationReportPhase.ResourceVersionReport
        );
    }

    [Fact]
    public async Task ReadAsync_UnexpectedResourceNameIsRejected()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceive(
            BuildEncryptedClientFrames(
                (
                    LoginAccountMacAddressReportPacket.PacketIdentifier,
                    BuildMacAddressPayload(SessionUid, "001122AABBCC")
                ),
                (
                    LoginAccountResourceVersionReportPacket.PacketIdentifier,
                    BuildResourceVersionPayload(SessionUid, 5517, "patch.dat")
                )
            )
        );

        LoginPostAuthenticationReportReader reader = new(session);

        LoginPostAuthenticationReportReadResult result = await reader.ReadAsync(
            SessionUid,
            TestContext.Current.CancellationToken
        );

        AssertFailure(
            result,
            LoginPostAuthenticationReportReadStatus.UnexpectedResourceName,
            LoginPostAuthenticationReportPhase.ResourceVersionReport
        );
    }

    [Fact]
    public async Task ReadAsync_PropagatesTransportReceiveFailure()
    {
        IOException failure = new("receive failed");

        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        connection.QueueReceiveFailure(failure);

        LoginPostAuthenticationReportReader reader = new(session);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            reader.ReadAsync(SessionUid, TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ReadAsync_PropagatesCallerCancellation()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection,
            new FakeLoginSeedGenerator(LoginSeed),
            TestContext.Current.CancellationToken
        );

        LoginPostAuthenticationReportReader reader = new(session);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<LoginPostAuthenticationReportReadResult> readTask = reader
            .ReadAsync(SessionUid, cancellation.Token)
            .AsTask();

        Assert.False(readTask.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    private static void AssertFailure(
        LoginPostAuthenticationReportReadResult result,
        LoginPostAuthenticationReportReadStatus expectedStatus,
        LoginPostAuthenticationReportPhase expectedPhase
    )
    {
        Assert.Equal(expectedStatus, result.Status);

        Assert.Equal(expectedPhase, result.FailurePhase);

        Assert.Null(result.Reports);

        if (expectedStatus != LoginPostAuthenticationReportReadStatus.UnexpectedPacket)
        {
            Assert.Null(result.UnexpectedPacketId);
        }
    }

    private static byte[] BuildMacAddressPayload(uint sessionUid, string macAddress)
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionUid);

        Encoding.ASCII.GetBytes(
            macAddress,
            payload.AsSpan(
                LoginAccountMacAddressReportPacket.MacAddressOffset,
                LoginAccountMacAddressReportPacket.MacAddressFieldLength
            )
        );

        return payload;
    }

    private static byte[] BuildResourceVersionPayload(
        uint sessionUid,
        int resourceVersion,
        string resourceName
    )
    {
        byte[] payload = new byte[LoginAccountResourceVersionReportPacket.PayloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionUid);

        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceVersionOffset),
            resourceVersion
        );

        Encoding.ASCII.GetBytes(
            resourceName,
            payload.AsSpan(
                LoginAccountResourceVersionReportPacket.ResourceNameOffset,
                LoginAccountResourceVersionReportPacket.ResourceNameFieldLength
            )
        );

        return payload;
    }

    private static byte[] BuildEncryptedClientFrames(
        params (ushort PacketId, byte[] Payload)[] frames
    )
    {
        int totalLength = 0;

        foreach ((ushort _, byte[] payload) in frames)
        {
            totalLength = checked(totalLength + sizeof(ushort) + sizeof(ushort) + payload.Length);
        }

        byte[] stream = new byte[totalLength];

        int offset = 0;

        foreach ((ushort packetId, byte[] payload) in frames)
        {
            int frameLength = checked(sizeof(ushort) + sizeof(ushort) + payload.Length);

            Span<byte> frame = stream.AsSpan(offset, frameLength);

            BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frameLength));

            BinaryPrimitives.WriteUInt16LittleEndian(frame[sizeof(ushort)..], packetId);

            payload.CopyTo(frame[(sizeof(ushort) + sizeof(ushort))..]);

            offset += frameLength;
        }

        EncryptClientOutbound(stream);

        return stream;
    }

    private static void EncryptClientOutbound(Span<byte> bytes)
    {
        ushort position = 0;

        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];

            value ^= s_streamA[(byte)position];
            value ^= s_streamB[(byte)(position >> 8)];

            value = (byte)((value >> 4) | (value << 4));

            value ^= 0xAB;

            bytes[index] = value;

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

    private sealed class FakeLoginSeedGenerator(uint seed) : ILoginSeedGenerator
    {
        public uint GenerateSeed()
        {
            return seed;
        }
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Channel<ReceiveOperation> _receiveOperations =
            Channel.CreateUnbounded<ReceiveOperation>();

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 40000);

        public async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            ReceiveOperation operation = await _receiveOperations
                .Reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            if (operation.Failure is not null)
            {
                throw operation.Failure;
            }

            if (operation.IsEndOfStream)
            {
                return 0;
            }

            byte[] bytes =
                operation.Bytes
                ?? throw new InvalidOperationException("Receive operation does not contain bytes.");

            if (bytes.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    "Test receive does not fit the supplied transport buffer."
                );
            }

            bytes.CopyTo(buffer);

            return bytes.Length;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
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

            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(bytes, Failure: null, IsEndOfStream: false)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test receive.");
            }
        }

        public void QueueReceiveFailure(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(Bytes: null, failure, IsEndOfStream: false)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test receive failure.");
            }
        }

        public void QueueEndOfStream()
        {
            if (
                !_receiveOperations.Writer.TryWrite(
                    new ReceiveOperation(Bytes: null, Failure: null, IsEndOfStream: true)
                )
            )
            {
                throw new InvalidOperationException("Unable to queue test end of stream.");
            }
        }

        private readonly record struct ReceiveOperation(
            byte[]? Bytes,
            Exception? Failure,
            bool IsEndOfStream
        );
    }
}
