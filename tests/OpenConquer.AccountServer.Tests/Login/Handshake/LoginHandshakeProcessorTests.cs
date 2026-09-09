using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Threading.Channels;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Handshake;

public sealed class LoginHandshakeProcessorTests
{
    private const uint LoginSeed = 0x0012_34AB;
    private const uint AccountId = 42;
    private const ulong AccountStateRevision = 7;
    private const ulong PasswordCredentialRevision = 3;
    private const uint SessionUid = 0x1122_3344;
    private const uint AuthenticationKey = 0x5566_7788;
    private const uint GameServerPort = 5816;
    private const string AccountName = "testacc";
    private const string Password = "password1";
    private const string ServerName = "Conquer";
    private const string GameServerIp = "127.0.0.1";
    private const string EncryptedCredential = "22DB42ACB82F421D" + "AEF13F7A611D5A03" + "2F45309A4D0DDC65" + "2F45309A4D0DDC65";

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        LoginHandshakeConfiguration configuration = CreateConfiguration();
        GameLoginTicketIssuer issuer = CreateIssuer(new FakeGrantStore());

        Assert.Throws<ArgumentNullException>(() => new LoginHandshakeProcessor(null!, issuer, configuration));
        Assert.Throws<ArgumentNullException>(() => new LoginHandshakeProcessor(new FakeAuthenticator(AccountAuthenticationResult.InvalidCredentials()), null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new LoginHandshakeProcessor(new FakeAuthenticator(AccountAuthenticationResult.InvalidCredentials()), issuer, null!));
    }

    [Fact]
    public async Task ProcessAsync_SuccessAuthenticatesGrantsBeforeResponseAndConsumesPostAuthenticationReports()
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        QueueAccountRequest(connection, clientCipher);
        QueuePostAuthenticationReports(connection, clientCipher, SessionUid);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.Succeeded(
            AccountId, AccountName, AccountStateRevision, PasswordCredentialRevision));
        FakeGrantStore grantStore = new(blockGrant: true);
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        Task processTask = processor.ProcessAsync(session, TestContext.Current.CancellationToken).AsTask();

        await grantStore.GrantStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WireFrameHeader.Size + sizeof(uint), connection.SentBytes.Length);

        grantStore.ReleaseGrant();
        await processTask;

        Assert.Equal(1, authenticator.CallCount);
        Assert.Equal(AccountName, authenticator.AccountName);
        Assert.Equal(Password, authenticator.Password);
        Assert.Equal(IPAddress.Loopback, authenticator.RemoteAddress);

        Assert.Equal(1, grantStore.CallCount);
        Assert.Equal(AccountId, grantStore.Request!.AccountId);
        Assert.Equal(AccountName, grantStore.Request.Username);
        Assert.Equal(SessionUid, grantStore.Request.SessionUid);
        Assert.Equal(AuthenticationKey, grantStore.Request.AuthenticationKey);
        Assert.Equal(AccountStateRevision, grantStore.ExpectedAccountStateRevision);
        Assert.Equal(PasswordCredentialRevision, grantStore.ExpectedPasswordCredentialRevision);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(SessionUid, response.SessionUid);
        Assert.Equal(AuthenticationKey, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(GameServerPort, response.GameServerPort);
        Assert.Equal(AuthenticationKey, response.AdditionalSessionField);
        Assert.Equal(GameServerIp, response.GameServerIp);
    }

    [Theory]
    [InlineData(LoginAccountRequestPacket.PacketIdentifier, LoginAccountAuthenticationFailureCode.InvalidAccount)]
    [InlineData((ushort)1084, LoginAccountAuthenticationFailureCode.InvalidCredentials)]
    [InlineData((ushort)1098, LoginAccountAuthenticationFailureCode.InvalidCredentials)]
    [InlineData((ushort)7777, LoginAccountAuthenticationFailureCode.InvalidAccount)]
    public async Task ProcessAsync_InvalidOrUnsupportedRequestReturnsVerifiedFailure(ushort packetId, LoginAccountAuthenticationFailureCode expectedFailure)
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        connection.QueueReceive(BuildClientFrame(clientCipher, packetId, ReadOnlySpan<byte>.Empty));

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.Succeeded(
            AccountId, AccountName, AccountStateRevision, PasswordCredentialRevision));
        FakeGrantStore grantStore = new();
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(0u, response.SessionUid);
        Assert.Equal((uint)expectedFailure, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(GameServerPort, response.GameServerPort);
        Assert.Equal(0u, response.AdditionalSessionField);
        Assert.Equal(GameServerIp, response.GameServerIp);
        Assert.Equal(0, authenticator.CallCount);
        Assert.Equal(0, grantStore.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_InvalidCredentialsReturnsNativeFailureWithoutGrant()
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        QueueAccountRequest(connection, clientCipher);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.InvalidCredentials());
        FakeGrantStore grantStore = new();
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(0u, response.SessionUid);
        Assert.Equal((uint)LoginAccountAuthenticationFailureCode.InvalidCredentials, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(1, authenticator.CallCount);
        Assert.Equal(0, grantStore.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_BannedAccountReturnsNativeBannedFailureWithoutGrant()
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        QueueAccountRequest(connection, clientCipher);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.Banned());
        FakeGrantStore grantStore = new();
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(0u, response.SessionUid);
        Assert.Equal((uint)LoginAccountAuthenticationFailureCode.Banned, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(1, authenticator.CallCount);
        Assert.Equal(0, grantStore.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_AuthenticationStateChangedBeforeGrantReturnsGenericCredentialFailure()
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        QueueAccountRequest(connection, clientCipher);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.Succeeded(
            AccountId, AccountName, AccountStateRevision, PasswordCredentialRevision));
        FakeGrantStore grantStore = new(authenticationStateChanged: true);
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(0u, response.SessionUid);
        Assert.Equal((uint)LoginAccountAuthenticationFailureCode.InvalidCredentials, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(1, authenticator.CallCount);
        Assert.Equal(1, grantStore.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CleanEndOfStreamReturnsWithoutAuthenticationOrResponse()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        connection.QueueEndOfStream();

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.InvalidCredentials());
        FakeGrantStore grantStore = new();
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(0, authenticator.CallCount);
        Assert.Equal(0, grantStore.CallCount);
        Assert.Equal(WireFrameHeader.Size + sizeof(uint), connection.SentBytes.Length);
    }

    [Fact]
    public async Task ProcessAsync_CallerCancellationPropagates()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.InvalidCredentials());
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(new FakeGrantStore()), CreateConfiguration());

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task processTask = processor.ProcessAsync(session, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processTask);
        Assert.Equal(0, authenticator.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CredentialPhaseTimeoutTerminatesConnectionFlow()
    {
        TestTransportConnection connection = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.InvalidCredentials());
        LoginHandshakeConfiguration configuration = new(IPAddress.Loopback, (int)GameServerPort, TimeSpan.FromMilliseconds(50));
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(new FakeGrantStore()), configuration);

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(0, authenticator.CallCount);
        Assert.Equal(WireFrameHeader.Size + sizeof(uint), connection.SentBytes.Length);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPostAuthenticationTelemetryDoesNotUndoSuccessfulHandoff()
    {
        TestTransportConnection connection = new();
        ClientCipher clientCipher = new();

        await using LoginConnectionSession session = await LoginConnectionSession.OpenAsync(
            connection, new FakeLoginSeedGenerator(LoginSeed), TestContext.Current.CancellationToken);

        QueueAccountRequest(connection, clientCipher);
        connection.QueueReceive(BuildClientFrame(clientCipher, 7777, ReadOnlySpan<byte>.Empty));

        FakeAuthenticator authenticator = new(AccountAuthenticationResult.Succeeded(
            AccountId, AccountName, AccountStateRevision, PasswordCredentialRevision));
        FakeGrantStore grantStore = new();
        LoginHandshakeProcessor processor = new(authenticator, CreateIssuer(grantStore), CreateConfiguration());

        await processor.ProcessAsync(session, TestContext.Current.CancellationToken);

        AuthenticationResponse response = ReadAuthenticationResponse(connection);
        Assert.Equal(SessionUid, response.SessionUid);
        Assert.Equal(AuthenticationKey, response.AuthenticationKeyOrFailureCode);
        Assert.Equal(1, grantStore.CallCount);
    }

    private static LoginHandshakeConfiguration CreateConfiguration()
    {
        return new LoginHandshakeConfiguration(IPAddress.Parse(GameServerIp), (int)GameServerPort, TimeSpan.FromSeconds(5));
    }

    private static GameLoginTicketIssuer CreateIssuer(FakeGrantStore grantStore)
    {
        return new GameLoginTicketIssuer(grantStore, new FakeTokenGenerator());
    }

    private static void QueueAccountRequest(TestTransportConnection connection, ClientCipher cipher)
    {
        connection.QueueReceive(BuildClientFrame(cipher, LoginAccountRequestPacket.PacketIdentifier, CreateAccountRequestPayload()));
    }

    private static void QueuePostAuthenticationReports(TestTransportConnection connection, ClientCipher cipher, uint sessionUid)
    {
        connection.QueueReceive(BuildClientFrame(cipher, LoginAccountMacAddressReportPacket.PacketIdentifier, CreateMacAddressReportPayload(sessionUid)));
        connection.QueueReceive(BuildClientFrame(cipher, LoginAccountResourceVersionReportPacket.PacketIdentifier, CreateResourceVersionReportPayload(sessionUid)));
    }

    private static byte[] CreateAccountRequestPayload()
    {
        byte[] payload = new byte[LoginAccountRequestPacket.PayloadLength];
        WriteAscii(AccountName, payload.AsSpan(LoginAccountRequestPacket.AccountNameOffset, LoginAccountRequestPacket.AccountNameLength));
        Convert.FromHexString(EncryptedCredential).CopyTo(payload.AsSpan(LoginAccountRequestPacket.CredentialFieldOffset, LoginAccountRequestPacket.StandardCredentialTransformLength));
        WriteAscii(ServerName, payload.AsSpan(LoginAccountRequestPacket.ServerNameOffset, LoginAccountRequestPacket.ServerNameLength));
        return payload;
    }

    private static byte[] CreateMacAddressReportPayload(uint sessionUid)
    {
        byte[] payload = new byte[LoginAccountMacAddressReportPacket.PayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionUid);
        WriteAscii("001122AABBCC", payload.AsSpan(LoginAccountMacAddressReportPacket.MacAddressOffset, LoginAccountMacAddressReportPacket.MacAddressFieldLength));
        return payload;
    }

    private static byte[] CreateResourceVersionReportPayload(uint sessionUid)
    {
        byte[] payload = new byte[LoginAccountResourceVersionReportPacket.PayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionUid);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceVersionOffset), 5517);
        WriteAscii("res.dat", payload.AsSpan(LoginAccountResourceVersionReportPacket.ResourceNameOffset, LoginAccountResourceVersionReportPacket.ResourceNameFieldLength));
        return payload;
    }

    private static byte[] BuildClientFrame(ClientCipher cipher, ushort packetId, ReadOnlySpan<byte> payload)
    {
        int frameLength = checked(WireFrameHeader.Size + payload.Length);
        byte[] frame = new byte[frameLength];

        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frameLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(sizeof(ushort)), packetId);
        payload.CopyTo(frame.AsSpan(WireFrameHeader.Size));
        cipher.EncryptOutbound(frame);

        return frame;
    }

    private static AuthenticationResponse ReadAuthenticationResponse(TestTransportConnection connection)
    {
        byte[] plaintext = connection.SentBytes;
        ClientCipher cipher = new();
        cipher.DecryptInbound(plaintext);

        const int seedFrameLength = WireFrameHeader.Size + sizeof(uint);

        Assert.True(plaintext.Length >= seedFrameLength + WireFrameHeader.Size);
        Assert.Equal(seedFrameLength, BinaryPrimitives.ReadUInt16LittleEndian(plaintext));
        Assert.Equal(LoginSeedPacket.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(plaintext.AsSpan(sizeof(ushort))));

        ReadOnlySpan<byte> frame = plaintext.AsSpan(seedFrameLength);

        Assert.Equal(WireFrameHeader.Size + LoginAccountAuthenticationResponsePacket.PayloadSize, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.Equal(LoginAccountAuthenticationResponsePacket.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(frame[sizeof(ushort)..]));

        ReadOnlySpan<byte> payload = frame[WireFrameHeader.Size..];
        uint sessionUid = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        uint authenticationKeyOrFailureCode = BinaryPrimitives.ReadUInt32LittleEndian(payload[sizeof(uint)..]);
        uint gameServerPort = BinaryPrimitives.ReadUInt32LittleEndian(payload[(sizeof(uint) * 2)..]);
        uint additionalSessionField = BinaryPrimitives.ReadUInt32LittleEndian(payload[(sizeof(uint) * 3)..]);
        string gameServerIp = ReadAscii(payload[(sizeof(uint) * 4)..LoginAccountAuthenticationResponsePacket.PayloadSize]);

        return new AuthenticationResponse(sessionUid, authenticationKeyOrFailureCode, gameServerPort, additionalSessionField, gameServerIp);
    }

    private static string ReadAscii(ReadOnlySpan<byte> value)
    {
        int terminator = value.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? value : value[..terminator]);
    }

    private static void WriteAscii(string value, Span<byte> destination)
    {
        _ = Encoding.ASCII.GetBytes(value, destination);
    }

    private readonly record struct AuthenticationResponse(
        uint SessionUid,
        uint AuthenticationKeyOrFailureCode,
        uint GameServerPort,
        uint AdditionalSessionField,
        string GameServerIp);

    private sealed class FakeAuthenticator(AccountAuthenticationResult result) : IAccountAuthenticator
    {
        public int CallCount { get; private set; }
        public string? AccountName { get; private set; }
        public string? Password { get; private set; }
        public IPAddress? RemoteAddress { get; private set; }

        public ValueTask<AccountAuthenticationResult> AuthenticateAsync(
            string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            AccountName = accountName;
            Password = new string(password.Span);
            RemoteAddress = remoteAddress;

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeGrantStore(bool blockGrant = false, bool authenticationStateChanged = false) : IGameLoginTicketGrantStore
    {
        private static readonly DateTimeOffset s_issuedAtUtc = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

        private readonly TaskCompletionSource _grantStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _grantRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public GameLoginTicketGrantRequest? Request { get; private set; }
        public ulong ExpectedAccountStateRevision { get; private set; }
        public ulong ExpectedPasswordCredentialRevision { get; private set; }
        public Task GrantStarted => _grantStarted.Task;

        public void ReleaseGrant() => _grantRelease.TrySetResult();

        public async ValueTask<GameLoginTicketGrantResult> TryGrantAsync(
            GameLoginTicketGrantRequest request,
            TimeSpan ticketLifetime,
            ulong expectedAccountStateRevision,
            ulong expectedPasswordCredentialRevision,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            ExpectedAccountStateRevision = expectedAccountStateRevision;
            ExpectedPasswordCredentialRevision = expectedPasswordCredentialRevision;
            _grantStarted.TrySetResult();

            if (blockGrant)
            {
                await _grantRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (authenticationStateChanged)
            {
                return GameLoginTicketGrantResult.AuthenticationStateChanged();
            }

            GameLoginTicket ticket = new(
                request.AccountId, request.Username, request.SessionUid, request.AuthenticationKey,
                s_issuedAtUtc, s_issuedAtUtc + ticketLifetime);

            return GameLoginTicketGrantResult.Granted(ticket);
        }
    }

    private sealed class FakeTokenGenerator : IGameLoginTicketTokenGenerator
    {
        public uint GenerateSessionUid() => SessionUid;
        public uint GenerateAuthenticationKey() => AuthenticationKey;
    }

    private sealed class FakeLoginSeedGenerator(uint seed) : ILoginSeedGenerator
    {
        public uint GenerateSeed() => seed;
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Channel<ReceiveOperation> _receives = Channel.CreateUnbounded<ReceiveOperation>();
        private readonly ArrayBufferWriter<byte> _sent = new();

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 40000);
        public byte[] SentBytes => _sent.WrittenSpan.ToArray();

        public void QueueReceive(byte[] bytes) => _receives.Writer.TryWrite(new ReceiveOperation(bytes, false));
        public void QueueEndOfStream() => _receives.Writer.TryWrite(new ReceiveOperation(null, true));

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReceiveOperation operation = await _receives.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (operation.IsEndOfStream)
            {
                return 0;
            }

            byte[] bytes = operation.Bytes ?? throw new InvalidOperationException("Receive operation contains no bytes.");
            bytes.CopyTo(buffer);
            return bytes.Length;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Span<byte> destination = _sent.GetSpan(buffer.Length);
            buffer.Span.CopyTo(destination);
            _sent.Advance(buffer.Length);

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _receives.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private readonly record struct ReceiveOperation(byte[]? Bytes, bool IsEndOfStream);
    }

    private sealed class ClientCipher
    {
        private static readonly byte[] s_streamA = BuildStreamA();
        private static readonly byte[] s_streamB = BuildStreamB();

        private ushort _outboundPosition;
        private ushort _inboundPosition;

        public void EncryptOutbound(Span<byte> buffer) => Transform(buffer, ref _outboundPosition);
        public void DecryptInbound(Span<byte> buffer) => Transform(buffer, ref _inboundPosition);

        private static void Transform(Span<byte> buffer, ref ushort position)
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                byte value = buffer[index];
                value ^= s_streamA[(byte)position];
                value ^= s_streamB[(byte)(position >> 8)];
                value = (byte)((value >> 4) | (value << 4));
                value ^= 0xAB;
                buffer[index] = value;
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
    }
}
