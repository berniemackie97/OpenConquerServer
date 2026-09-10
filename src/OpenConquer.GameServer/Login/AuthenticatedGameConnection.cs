using System.Net;
using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Owns one authenticated GameServer connection and the canonical identity
/// established by successful single-use game-login ticket redemption.
/// </summary>
internal sealed class AuthenticatedGameConnection : IAsyncDisposable
{
    private readonly GameConnectionSession _session;

    public AuthenticatedGameConnection(uint accountId, string username, uint sessionUid, ushort localeTag, ulong hardwareAddress,
        int resourceVersion, GameConnectionSession session)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An authenticated game connection requires a persisted account identifier.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "An authenticated game connection requires a nonzero session UID.");
        }

        _session = session ?? throw new ArgumentNullException(nameof(session));

        AccountId = accountId;
        Username = username;
        SessionUid = sessionUid;
        LocaleTag = localeTag;
        HardwareAddress = hardwareAddress;
        ResourceVersion = resourceVersion;
    }

    public uint AccountId { get; }
    public string Username { get; }
    public uint SessionUid { get; }
    public ushort LocaleTag { get; }
    public ulong HardwareAddress { get; }
    public int ResourceVersion { get; }

    public EndPoint LocalEndPoint => _session.LocalEndPoint;
    public EndPoint RemoteEndPoint => _session.RemoteEndPoint;

    public ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default) => _session.ReadAsync(cancellationToken);

    public ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default) => _session.WriteAsync(packet, cancellationToken);

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
