using System.Runtime.ExceptionServices;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.GameServer.Login.Character;

/// <summary>
/// An authenticated connection for an existing character, with its login profile, its presence in this realm
/// and the record that its account is playing here.
/// </summary>
internal sealed class ExistingCharacterGameConnection : IAsyncDisposable
{
    private readonly AuthenticatedGameConnection _connection;
    private readonly ICharacterPresenceLease _presenceLease;
    private readonly AccountSessionLease _accountSession;
    private readonly Lock _disposeGate = new();
    private Task? _disposeTask;
    private int _disposeState;

    public ExistingCharacterGameConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile, ICharacterPresenceLease presenceLease,
        AccountSessionLease accountSession, CharacterLocationWriteAuthority? locationAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(presenceLease);
        ArgumentNullException.ThrowIfNull(accountSession);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The existing-character connection profile belongs to a different authenticated account.", nameof(profile));
        }

        if (presenceLease.CharacterId != profile.Identity.CharacterId)
        {
            throw new ArgumentException("The presence lease belongs to a different character.", nameof(presenceLease));
        }

        if (accountSession.AccountId != connection.AccountId || accountSession.SessionUid != connection.SessionUid)
        {
            throw new ArgumentException("The account session record belongs to a different login.", nameof(accountSession));
        }

        if (locationAuthority is not null
            && (locationAuthority.AccountId != connection.AccountId || locationAuthority.CharacterId != profile.Identity.CharacterId))
        {
            throw new ArgumentException("The writer generation belongs to another authenticated character.", nameof(locationAuthority));
        }

        LocationAuthority = locationAuthority;
        _connection = connection;
        _presenceLease = presenceLease;
        _accountSession = accountSession;
        Profile = profile;
    }

    public CharacterLoginProfile Profile { get; }
    public CharacterLocationWriteAuthority? LocationAuthority { get; }
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

    /// <summary>
    /// Writes one last packet after the presence was revoked, when ordinary writes are already cancelled.
    /// </summary>
    public ValueTask WriteFarewellAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ThrowIfDisposed();

        if (!IsRevoked)
        {
            throw new InvalidOperationException("A farewell packet may only be written after the presence was revoked.");
        }

        return _connection.WriteAsync(packet, cancellationToken);
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
        finally
        {
            await _accountSession.DisposeAsync().ConfigureAwait(false);
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
