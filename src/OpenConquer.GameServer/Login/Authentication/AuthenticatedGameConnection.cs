using System.Net;
using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.Login.Authentication;

/// <summary>
/// Owns one authenticated GameServer connection and the canonical identity
/// established by successful single-use game-login ticket redemption.
/// </summary>
internal sealed class AuthenticatedGameConnection : IAsyncDisposable
{
    private const int RepeatedProofUnbound = 0;
    private const int RepeatedProofArmed = 1;
    private const int RepeatedProofConsumed = 2;

    private readonly GameConnectionSession _session;
    private readonly Lock _repeatedProofGate = new();
    private uint _repeatedProofAuthenticationKey;
    private int _repeatedProofState;

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

    /// <summary>
    /// Retains the minimum proof material needed to verify exactly one same-session
    /// post-creation 1052, without redeeming the consumed ticket again.
    /// Called only after successful ticket redemption.
    /// </summary>
    internal void ArmRepeatedLoginProof(GameLoginProof firstProof)
    {
        if (firstProof.SessionUid != SessionUid || firstProof.AuthenticationKey == 0 ||
            firstProof.Mode != GameLoginProof.ExpectedMode || firstProof.LocaleTag != LocaleTag ||
            firstProof.HardwareAddress != HardwareAddress || firstProof.ResourceVersion != ResourceVersion)
        {
            throw new ArgumentException("The initial login proof does not match this authenticated session.", nameof(firstProof));
        }

        lock (_repeatedProofGate)
        {
            if (_repeatedProofState != RepeatedProofUnbound)
            {
                throw new InvalidOperationException("The repeated game-login proof may only be armed once.");
            }

            _repeatedProofAuthenticationKey = firstProof.AuthenticationKey;
            _repeatedProofState = RepeatedProofArmed;
        }
    }

    /// <summary>
    /// Consumes the one permitted repeat attempt, including an incorrect attempt.
    /// No account ticket store is consulted and no second attempt can succeed.
    /// </summary>
    internal bool TryConsumeRepeatedLoginProof(GameLoginProof repeatedProof)
    {
        lock (_repeatedProofGate)
        {
            if (_repeatedProofState != RepeatedProofArmed)
            {
                return false;
            }

            uint expectedKey = _repeatedProofAuthenticationKey;
            _repeatedProofAuthenticationKey = 0;
            _repeatedProofState = RepeatedProofConsumed;

            return repeatedProof.SessionUid == SessionUid &&
                   repeatedProof.AuthenticationKey == expectedKey &&
                   repeatedProof.Mode == GameLoginProof.ExpectedMode;
        }
    }

    /// <summary>
    /// Clears the proof when the session cannot enter the creation path, for example
    /// after it is resolved to an existing character.
    /// </summary>
    internal void DiscardRepeatedLoginProof()
    {
        lock (_repeatedProofGate)
        {
            _repeatedProofAuthenticationKey = 0;
            _repeatedProofState = RepeatedProofConsumed;
        }
    }

    public ValueTask DisposeAsync()
    {
        DiscardRepeatedLoginProof();
        return _session.DisposeAsync();
    }
}
