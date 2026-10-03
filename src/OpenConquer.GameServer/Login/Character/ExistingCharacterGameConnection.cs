using System.Runtime.ExceptionServices;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;

namespace OpenConquer.GameServer.Login.Character;

/// <summary>
/// Owns one authenticated existing-character connection together with its canonical profile and authoritative realm-presence lifetime.
/// </summary>
internal sealed class ExistingCharacterGameConnection : IAsyncDisposable
{
    private readonly AuthenticatedGameConnection _connection;
    private readonly ICharacterPresenceLease _presenceLease;
    private readonly Lock _disposeGate = new();
    private Task? _disposeTask;
    private int _disposeState;

    public ExistingCharacterGameConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile, ICharacterPresenceLease presenceLease)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(presenceLease);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The existing-character connection profile belongs to a different authenticated account.", nameof(profile));
        }

        if (presenceLease.CharacterId != profile.Identity.CharacterId)
        {
            throw new ArgumentException("The presence lease belongs to a different character.", nameof(presenceLease));
        }

        _connection = connection;
        _presenceLease = presenceLease;
        Profile = profile;
    }

    public CharacterLoginProfile Profile { get; }
    public bool IsRevoked => _presenceLease.IsRevoked;
    public CancellationToken RevocationToken => _presenceLease.RevocationToken;

    public void ThrowIfRevoked()
    {
        ThrowIfDisposed();
        RevocationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RevocationToken);
        return await _connection.ReadAsync(cancellation.Token).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ThrowIfDisposed();

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RevocationToken);
        await _connection.WriteAsync(packet, cancellation.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;

        lock (_disposeGate)
        {
            disposeTask = _disposeTask ?? StartDispose();
        }

        return new ValueTask(disposeTask);
    }

    private Task StartDispose()
    {
        Volatile.Write(ref _disposeState, 1);
        Task disposeTask = DisposeCoreAsync();
        _disposeTask = disposeTask;
        return disposeTask;
    }

    private async Task DisposeCoreAsync()
    {
        Exception? presenceFailure = null;

        try
        {
            _presenceLease.Dispose();
        }
        catch (Exception exception)
        {
            presenceFailure = exception;
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception connectionFailure)
        {
            if (presenceFailure is not null)
            {
                throw CreateDisposalFailure(presenceFailure, connectionFailure);
            }

            throw;
        }

        if (presenceFailure is not null)
        {
            ExceptionDispatchInfo.Capture(presenceFailure).Throw();
        }
    }

    private static AggregateException CreateDisposalFailure(Exception presenceFailure, Exception connectionFailure)
    {
        List<Exception> failures = [presenceFailure];

        if (connectionFailure is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(connectionFailure);
        }

        return new AggregateException("Existing-character presence release and connection disposal both failed.", failures);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
    }
}
