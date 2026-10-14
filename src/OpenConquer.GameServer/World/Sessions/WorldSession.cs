using System.Diagnostics;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Movement;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.World.Sessions;

/// <summary>
/// Owns an authenticated connection, its completed login snapshots, and one authoritative world residency.
/// Revocation and world shutdown initiate the same supervised, idempotent teardown.
/// </summary>
/// <remarks>
/// Presence is revoked only when the account logs in again, here or in another realm. The session then sends
/// the native kick notice (1115) before closing, as the 5517 client expects when it is logged out by another login.
/// </remarks>
internal sealed class WorldSession : IAsyncDisposable
{
    private static readonly GameKickoutNoticePacket s_otherLoginNotice = new(GameKickoutReason.OtherLogin, "Your account was logged in from another location.");
    private static readonly TimeSpan s_noticeTimeout = TimeSpan.FromSeconds(2);

    private readonly ExistingCharacterGameConnection _connection;
    private readonly WorldRuntime _runtime;
    private readonly WorldWalkPacer _walkPacer = new();
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CancellationTokenRegistration _presenceRegistration;
    private CancellationTokenRegistration _runtimeRegistration;
    private int _disposalStarted;
    private int _supersededByLogin;

    internal WorldSession(WorldRuntime runtime, ExistingCharacterGameConnection connection, ExistingCharacterBootstrapCompleteConnection bootstrap,
        WorldCharacterOwnership ownership)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));

        if (ownership.CharacterId != bootstrap.Profile.Identity.CharacterId || ownership.MapId != bootstrap.Map.MapId || ownership.RuntimeId == Guid.Empty)
        {
            throw new ArgumentException("World ownership does not match the authenticated bootstrap character and map.", nameof(ownership));
        }

        Ownership = ownership;
    }

    public ExistingCharacterBootstrapCompleteConnection Bootstrap { get; }
    public CharacterLoginProfile Profile => Bootstrap.Profile;
    public WorldMapContent Map => Bootstrap.Map;
    public WorldCharacterOwnership Ownership { get; }
    public Task Completion => _completion.Task;
    public bool IsStopping => Volatile.Read(ref _disposalStarted) != 0;

    internal void Activate()
    {
        CancellationTokenRegistration presence = _connection.RevocationToken.UnsafeRegister(static state => ((WorldSession)state!).OnPresenceRevoked(), this);
        CancellationTokenRegistration runtime;

        try
        {
            runtime = _runtime.StoppingToken.UnsafeRegister(static state => ((WorldSession)state!).BeginDispose(), this);
        }
        catch
        {
            presence.Dispose();
            throw;
        }

        bool disposeRegistrations;

        lock (_gate)
        {
            disposeRegistrations = _disposalStarted != 0;

            if (!disposeRegistrations)
            {
                _presenceRegistration = presence;
                _runtimeRegistration = runtime;
            }
        }

        if (disposeRegistrations)
        {
            presence.Dispose();
            runtime.Dispose();
        }

        if (_connection.IsRevoked)
        {
            OnPresenceRevoked();
        }
        else if (!_runtime.IsRunning)
        {
            BeginDispose();
        }
    }

    public void ThrowIfInactive()
    {
        ObjectDisposedException.ThrowIf(IsStopping, this);
        _connection.ThrowIfRevoked();

        if (!_runtime.IsRunning)
        {
            throw new InvalidOperationException("The authoritative world runtime is stopping.");
        }
    }

    /// <summary>
    /// Applies exactly one authenticated eight-direction ground step. The packet is echoed
    /// only after the authoritative partition commits the new position.
    /// </summary>
    internal async ValueTask ProcessWalkAsync(GameWalkPacket walk, CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();

        if (walk.EntityId != Ownership.CharacterId || walk.DirectionIndex >= 8 || walk.CommandSelector == GameWalkPacket.TwentyFourDirectionSelector)
        {
            throw new InvalidDataException("A walk command does not match the authenticated character or supported native direction mode.");
        }

        if (!_walkPacer.TryAccept(walk.MovementTick, Stopwatch.GetTimestamp()))
        {
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);
        WorldCharacterSnapshot? committed = await _runtime.TryWalkCharacterAsync(Ownership, walk.DirectionIndex, linked.Token).ConfigureAwait(false);

        if (committed is not null)
        {
            await WriteAsync(walk, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInactive();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);

        GameInboundFrame? frame;

        try
        {
            frame = await _connection.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception operationFailure)
        {
            await FailConnectionAsync(operationFailure).ConfigureAwait(false);
            throw;
        }

        if (frame is null)
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        return frame;
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ThrowIfInactive();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.StoppingToken);

        await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            await _connection.WriteAsync(packet, linked.Token).ConfigureAwait(false);
        }
        catch (Exception operationFailure)
        {
            _writeGate.Release();
            await FailConnectionAsync(operationFailure).ConfigureAwait(false);
            throw;
        }

        _writeGate.Release();
    }

    public ValueTask DisposeAsync()
    {
        BeginDispose();
        return new ValueTask(_completion.Task);
    }

    // Best effort: the client is told why it was disconnected, but a connection that cannot take one more frame
    // within the time limit is closed without it.
    private async Task SendOtherLoginNoticeAsync()
    {
        using CancellationTokenSource timeout = new(s_noticeTimeout);

        try
        {
            await _writeGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await _connection.WriteFarewellAsync(s_otherLoginNotice, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task FailConnectionAsync(Exception operationFailure)
    {
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            throw new AggregateException("World-session I/O failed and cleanup also failed.", operationFailure, cleanupFailure);
        }
    }

    private void OnPresenceRevoked()
    {
        lock (_gate)
        {
            // The session's own teardown also releases its presence; only a revocation that arrives first
            // means that another login took the account over.
            if (_disposalStarted == 0)
            {
                Volatile.Write(ref _supersededByLogin, 1);
            }
        }

        BeginDispose();
    }

    private void BeginDispose()
    {
        lock (_gate)
        {
            if (_disposalStarted != 0)
            {
                return;
            }

            Volatile.Write(ref _disposalStarted, 1);
            _ = Task.Run(DisposeCoreAsync);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception> failures = [];

        if (Volatile.Read(ref _supersededByLogin) != 0)
        {
            await SendOtherLoginNoticeAsync().ConfigureAwait(false);
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _runtime.ReleaseCharacterAsync(Ownership).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);

            try
            {
                await _runtime.StopPartitionExecutionAsync().ConfigureAwait(false);
            }
            catch (Exception stopFailure)
            {
                failures.Add(stopFailure);
            }
        }

        try
        {
            CancellationTokenRegistration presence;
            CancellationTokenRegistration runtime;

            lock (_gate)
            {
                presence = _presenceRegistration;
                runtime = _runtimeRegistration;
                _presenceRegistration = default;
                _runtimeRegistration = default;
            }

            presence.Dispose();
            runtime.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            if (failures.Count == 0)
            {
                _completion.TrySetResult();
            }
            else if (failures.Count == 1)
            {
                _completion.TrySetException(failures[0]);
            }
            else
            {
                _completion.TrySetException(new AggregateException("World session connection and ownership cleanup encountered multiple failures.", failures));
            }
        }
        finally
        {
            _runtime.UntrackSession(Ownership, _completion.Task);
        }
    }
}
