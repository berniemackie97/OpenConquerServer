using System.Buffers.Binary;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Time;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap;

/// <summary>
/// A secured GameServer connection with a test client on the other end, for driving login steps.
/// </summary>
internal sealed class LoginStepHarness : IAsyncDisposable
{
    public const uint AccountId = 42;
    public const string Username = "Bernie";
    public const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    public const uint MapId = 1002;
    public const uint MapDataId = 1015;
    public const ulong MapFlags = 0x1122334455667788;

    private int _readOffset;

    private LoginStepHarness(FakeGameTransportConnection transport, GameClientTestPeer client, AuthenticatedGameConnection connection)
    {
        Transport = transport;
        Client = client;
        Connection = connection;
        _readOffset = transport.SentBytes.Length;
    }

    public FakeGameTransportConnection Transport { get; }
    public GameClientTestPeer Client { get; }
    public AuthenticatedGameConnection Connection { get; }
    public CharacterPresenceDirectory Presence { get; } = new();

    /// <summary>
    /// Number of encrypted bytes the server has sent since the handshake that <see cref="ReadServerPackets"/> has not returned yet.
    /// </summary>
    public int UnreadServerBytes => Transport.SentBytes.Length - _readOffset;

    public static async Task<LoginStepHarness> CreateAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(disposeFailure: disposeFailure);
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        GameClientTestPeer? client = null;

        try
        {
            using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
            client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

            await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);
            transport.QueueReceive(client.EncryptedKeyExchangeResponse);

            string clientPublicKeyHex = Assert.IsType<string>(await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
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

            AuthenticatedGameConnection connection = new(AccountId, Username, sessionUid: 0x1020_3040, localeTag: 0x6E45,
                hardwareAddress: 0x0000_6655_4433_2211, resourceVersion: 5517, session);

            return new LoginStepHarness(transport, client, connection);
        }
        catch
        {
            client?.Dispose();
            await session.DisposeAsync();
            throw;
        }
    }

    public static CharacterLoginProfile CreateProfile(uint mapId = MapId, ushort x = 430, ushort y = 378)
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(CharacterId, AccountId, Username),
            new CharacterAppearance(composite: 2011003, hair: 339),
            new CharacterProgression(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20,
                rebirthCount: 2, preRebirthLevel: 130),
            new CharacterAttributes(101, 102, 103, 104, 105),
            new CharacterVitals(Life: 1234, Mana: 567),
            new CharacterEconomy(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678),
            pkPoints: -25, titleId: 321, enlightenmentPoints: 1234, new CharacterLocation(mapId, x, y));
    }

    public static byte[] BuildActionPacket(ushort action, uint entityId = 0, uint parameterPair = 0, uint actionParameter = 0,
        uint timestamp = 0, ushort direction = 0, ushort positionX = 0, ushort positionY = 0, uint data1 = 0, uint data2 = 0,
        byte flag = 0, int length = GameAction.FixedPacketLength, byte stringCount = 0)
    {
        byte[] packet = new byte[length];
        WireFrameHeader.Write(packet, checked((ushort)length), GameAction.PacketIdentifier);

        if (length < GameAction.FixedPacketLength)
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

    public static ushort ReadPacketId(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2));

    public void QueueAction(ushort action, uint entityId = CharacterId, uint timestamp = 0)
    {
        QueuePacket(BuildActionPacket(action, entityId, timestamp: timestamp));
    }

    public void QueuePacket(byte[] packet) => Transport.QueueReceive(Client.EncryptClientFrame(packet));

    /// <summary>
    /// Decrypts everything the server sent since the previous call and splits it into packets without their signatures.
    /// </summary>
    public byte[][] ReadServerPackets()
    {
        byte[] sent = Transport.SentBytes;
        byte[] plaintext = Client.DecryptServerBytes(sent.AsSpan(_readOffset));
        _readOffset = sent.Length;

        List<byte[]> packets = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext.AsSpan(offset), out WireFrameHeader header));
            Assert.InRange(header.Length, WireFrameHeader.Size, GameWireProtocol.MaximumPacketLength);

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);

            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.AsSpan(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

            packets.Add(plaintext.AsSpan(offset, header.Length).ToArray());
            offset += wireLength;
        }

        return packets.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (Transport.DisposeCount == 0)
        {
            await Connection.DisposeAsync();
        }
    }

    internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    internal sealed class FixedGameTickSource(uint currentTick) : IGameTickSource
    {
        public uint CurrentTick { get; } = currentTick;
    }
}
