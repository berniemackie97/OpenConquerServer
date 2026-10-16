using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Login.Hosting;

/// <summary>
/// Runs bounded login work and supervises completed world-session lifetimes independently of login workers.
/// </summary>
internal sealed partial class GameConnectionWorkerPool(
    TransportConnectionAdmissionQueue queue,
    Func<ITransportConnection, CancellationToken, ValueTask<WorldSession?>> processConnection,
    GameNetworkOptions options,
    GameLoginMetrics metrics,
    GameServerFailureState failureState,
    IHostApplicationLifetime applicationLifetime,
    ILogger<GameConnectionWorkerPool> logger)
{
    private readonly TransportConnectionAdmissionQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly Func<ITransportConnection, CancellationToken, ValueTask<WorldSession?>> _processConnection = processConnection ?? throw new ArgumentNullException(nameof(processConnection));
    private readonly GameNetworkOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly GameLoginMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly GameServerFailureState _failureState = failureState ?? throw new ArgumentNullException(nameof(failureState));
    private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
    private readonly ILogger<GameConnectionWorkerPool> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _sessions = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Task[] workers = new Task[_options.WorkerCount];

        for (int index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync(index, cancellationToken);
        }

        Exception? workerFailure = null;

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            workerFailure = exception;
        }

        Task[] sessions;

        lock (_gate)
        {
            sessions = [.. _sessions];
        }

        Exception? sessionFailure = null;

        try
        {
            await Task.WhenAll(sessions).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            sessionFailure = exception;
        }

        if (workerFailure is not null && sessionFailure is not null)
        {
            throw new AggregateException("GameServer login workers and world sessions both failed.", workerFailure, sessionFailure);
        }

        if (workerFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(workerFailure).Throw();
        }

        if (sessionFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(sessionFailure).Throw();
        }
    }

    private async Task RunWorkerAsync(int workerIndex, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ITransportConnection connection in _queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                EndPoint local = connection.LocalEndPoint;
                EndPoint remote = connection.RemoteEndPoint;
                long started = Stopwatch.GetTimestamp();

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ConnectionTimeout);

                try
                {
                    WorldSession? session = await _processConnection(connection, timeout.Token).ConfigureAwait(false);
                    double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                    if (session is null)
                    {
                        _metrics.RecordNotAdmitted(duration);
                    }
                    else
                    {
                        StartSession(session, cancellationToken);
                        _metrics.RecordCompleted(duration);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    _metrics.RecordTimeout(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    LogConnectionTimeout(_logger, workerIndex, local, remote, _options.ConnectionTimeout);
                }
                catch (Exception exception)
                {
                    _metrics.RecordFailure(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    LogConnectionFailure(_logger, workerIndex, local, remote, exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            _failureState.Record(failure);

            try
            {
                LogFatalWorkerFailure(_logger, workerIndex, failure);
            }
            finally
            {
                _applicationLifetime.StopApplication();
            }

            throw;
        }
    }

    private void StartSession(WorldSession session, CancellationToken cancellationToken)
    {
        Task task = RunSessionAsync(session, cancellationToken);

        lock (_gate)
        {
            _sessions.Add(task);
        }

        _metrics.RecordSessionStarted();

        _ = task.ContinueWith(static (completed, state) =>
        {
            ((GameConnectionWorkerPool)state!).SessionCompleted(completed);
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunSessionAsync(WorldSession session, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                while (true)
                {
                    using GameInboundFrame? frame = await session.ReadAsync(cancellationToken).ConfigureAwait(false);

                    if (frame is null)
                    {
                        return;
                    }

                    if (frame.PacketId == GameChatPacket.PacketIdentifier)
                    {
                        if (!GameInboundChat.TryParse(frame, out GameInboundChat chat))
                        {
                            throw new InvalidDataException("The GameServer received a malformed native chat packet.");
                        }

                        await session.ProcessChatAsync(chat, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.PacketId == GameWalkPacket.PacketIdentifier)
                    {
                        if (!GameWalkPacket.TryParse(frame.Packet.Span, out GameWalkPacket walk))
                        {
                            throw new InvalidDataException("The GameServer received a malformed native walk packet.");
                        }

                        await session.ProcessWalkAsync(walk, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.PacketId == GameAction.PacketIdentifier)
                    {
                        if (GameGhostTransitionRequest.TryParse(frame, out GameGhostTransitionRequest ghost))
                        {
                            await session.ProcessGhostTransitionAsync(ghost, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (GameJumpAction.TryParse(frame, out GameJumpAction jump))
                        {
                            await session.ProcessJumpAsync(jump, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (GamePositionReport.TryParse(frame, out GamePositionReport report))
                        {
                            await session.ProcessPositionReportAsync(report, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (frame.Header.Length == GameAction.FixedPacketLength &&
                            GameAction.TryParse(frame, out GameAction query, out _) &&
                            query.Action == 102 && query.StringCount == 0 &&
                            query.EntityId == session.Ownership.CharacterId && query.ParameterPair != 0)
                        {
                            session.QueryVisibleEntity(query.ParameterPair);
                            continue;
                        }

                        if (GameAvatarAction.TryParse(frame, out GameAvatarAction avatar))
                        {
                            await session.ProcessAvatarActionAsync(avatar, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        throw new InvalidDataException("The GameServer received an unsupported native action packet.");
                    }

                    throw new InvalidDataException("The GameServer received an unsupported gameplay packet.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || session.IsStopping)
            {
            }
            catch (ObjectDisposedException) when (session.IsStopping)
            {
            }
            catch (InvalidDataException exception)
            {
                LogUnsupportedGameplay(_logger, session.Ownership.CharacterId, exception);
            }
            catch (Exception exception)
            {
                LogWorldSessionFailure(_logger, session.Ownership.CharacterId, exception);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            _failureState.Record(failure);

            try
            {
                LogFatalSessionCleanup(_logger, failure);
            }
            finally
            {
                _applicationLifetime.StopApplication();
            }
        }
    }

    private void SessionCompleted(Task task)
    {
        lock (_gate)
        {
            _sessions.Remove(task);
        }

        _metrics.RecordSessionEnded();
    }

    [LoggerMessage(EventId = 6200, Level = LogLevel.Debug, Message = "GameServer login processing timed out after {Timeout} on worker {WorkerIndex}. Local: {LocalEndPoint}; remote: {RemoteEndPoint}.")]
    private static partial void LogConnectionTimeout(ILogger logger, int workerIndex, EndPoint localEndPoint, EndPoint remoteEndPoint, TimeSpan timeout);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Warning, Message = "GameServer login connection failed on worker {WorkerIndex}. Local: {LocalEndPoint}; remote: {RemoteEndPoint}.")]
    private static partial void LogConnectionFailure(ILogger logger, int workerIndex, EndPoint localEndPoint, EndPoint remoteEndPoint, Exception exception);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Debug, Message = "Unsupported gameplay traffic closed world session for character {CharacterId}.")]
    private static partial void LogUnsupportedGameplay(ILogger logger, uint characterId, Exception exception);

    [LoggerMessage(EventId = 6203, Level = LogLevel.Warning, Message = "World-session I/O failed for character {CharacterId}.")]
    private static partial void LogWorldSessionFailure(ILogger logger, uint characterId, Exception exception);

    [LoggerMessage(EventId = 6204, Level = LogLevel.Critical, Message = "Supervised GameServer world-session cleanup failed.")]
    private static partial void LogFatalSessionCleanup(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6205, Level = LogLevel.Critical, Message = "GameServer login worker {WorkerIndex} terminated unexpectedly.")]
    private static partial void LogFatalWorkerFailure(ILogger logger, int workerIndex, Exception exception);
}
