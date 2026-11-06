using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using OpenConquer.GameServer.Connections.Framing;
using OpenConquer.GameServer.Handshake;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Transport.Connections;
using OpenConquer.Transport.Pipelines;

namespace OpenConquer.GameServer.Connections;

/// <summary>
/// One GameServer connection: transport pumps, buffers, the handshake, the session cipher and secured framing.
/// </summary>
internal sealed class GameConnectionSession : IAsyncDisposable
{
    private readonly ITransportConnection _connection;
    private readonly Pipe _inputPipe;
    private readonly Pipe _outputPipe;
    private readonly CancellationTokenSource _lifetimeCancellation;
    private readonly GameClientKeyExchangeResponseReader _keyExchangeResponseReader;
    private readonly Task _inputPump;
    private readonly Task _outputPump;

    private readonly Lock _protocolGate = new();
    private readonly GameHandshakeProgress _handshake = new();
    private readonly Lock _operationGate = new();
    private readonly Lock _disposeGate = new();

    private GameSessionCipher? _sessionCipher;
    private GameSecuredFrameReader? _frameReader;
    private GameSecuredFrameWriter? _frameWriter;
    private TaskCompletionSource? _operationsDrained;
    private Task? _disposeTask;

    private int _activeOperationCount;
    private int _disposeState;
    private int _inputPumpFailureObserved;
    private int _outputPumpFailureObserved;

    private GameConnectionSession(ITransportConnection connection)
    {
        _connection = connection;
        _lifetimeCancellation = new CancellationTokenSource();

        _inputPipe = CreateInputPipe();
        _outputPipe = CreateOutputPipe();

        _keyExchangeResponseReader = new GameClientKeyExchangeResponseReader(_inputPipe.Reader);

        _inputPump = TransportConnectionInput.PumpAsync(connection, _inputPipe.Writer, _lifetimeCancellation.Token);
        _outputPump = TransportConnectionOutput.PumpAsync(connection, _outputPipe.Reader, _lifetimeCancellation.Token);
    }

    public EndPoint LocalEndPoint => _connection.LocalEndPoint;
    public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;

    /// <summary>
    /// Starts the transport pumps for <paramref name="connection"/>.
    /// </summary>
    /// <remarks>
    /// The session takes over the connection. If opening fails, the connection is disposed before the exception
    /// is rethrown.
    /// </remarks>
    public static async ValueTask<GameConnectionSession> OpenAsync(ITransportConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        GameConnectionSession? session = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            session = new GameConnectionSession(connection);

            return session;
        }
        catch (Exception openException)
        {
            try
            {
                if (session is null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception cleanupException)
            {
                throw CreateOpenFailure(openException, cleanupException);
            }

            throw;
        }
    }

    /// <summary>
    /// Sends the bootstrap-encrypted Diffie-Hellman challenge. This happens once, before secured framing starts.
    /// </summary>
    public async ValueTask SendHandshakeChallengeAsync(ReadOnlyMemory<byte> encryptedChallenge, CancellationToken cancellationToken = default)
    {
        if (encryptedChallenge.IsEmpty)
        {
            throw new ArgumentException("The GameServer handshake challenge cannot be empty.", nameof(encryptedChallenge));
        }

        EnterOperation();

        try
        {
            EnterChallengeWrite();

            try
            {
                await WriteHandshakeChallengeCoreAsync(encryptedChallenge, cancellationToken).ConfigureAwait(false);
                CompleteChallengeWrite();
            }
            catch
            {
                MarkProtocolTerminal();
                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Reads and validates the client's bootstrap-encrypted Diffie-Hellman response.
    /// </summary>
    public async ValueTask<string?> ReadClientKeyExchangeResponseAsync(CancellationToken cancellationToken = default)
    {
        EnterOperation();

        try
        {
            EnterKeyExchangeRead();

            try
            {
                string? clientPublicKeyHex = await _keyExchangeResponseReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (clientPublicKeyHex is null)
                {
                    MarkProtocolTerminal();
                    return null;
                }

                CompleteKeyExchangeRead();

                return clientPublicKeyHex;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ResetKeyExchangeReadAfterCancellation();
                throw;
            }
            catch
            {
                MarkProtocolTerminal();

                await ThrowIfInputPumpFaultedAsync().ConfigureAwait(false);

                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Switches the connection to secured framing with the negotiated cipher, which the session takes over.
    /// This can only happen once.
    /// </summary>
    public void CompleteHandshake(GameSessionCipher sessionCipher)
    {
        ArgumentNullException.ThrowIfNull(sessionCipher);

        lock (_protocolGate)
        {
            ThrowIfDisposed();

            _handshake.RequireCompletion();

            GameSecuredFrameReader? frameReader = null;

            try
            {
                frameReader = new GameSecuredFrameReader(_inputPipe.Reader, sessionCipher);
                GameSecuredFrameWriter frameWriter = new(_outputPipe.Writer, sessionCipher);

                _sessionCipher = sessionCipher;
                _frameReader = frameReader;
                _frameWriter = frameWriter;
                _handshake.MarkSecured();
            }
            catch
            {
                frameReader?.Dispose();
                throw;
            }
        }
    }

    public async ValueTask<GameInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterOperation();

        try
        {
            GameSecuredFrameReader frameReader = GetSecuredFrameReader();

            try
            {
                return await frameReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await ThrowIfInputPumpFaultedAsync().ConfigureAwait(false);

                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    public ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default) =>
        WriteCoreAsync(packet, second: null, cancellationToken);

    public ValueTask WriteBatchAsync(IPacket first, IPacket second, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(second);
        return WriteCoreAsync(first, second, cancellationToken);
    }

    private async ValueTask WriteCoreAsync(IPacket first, IPacket? second, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(first);
        EnterOperation();
        try
        {
            GameSecuredFrameWriter frameWriter = GetSecuredFrameWriter();
            try
            {
                if (second is null)
                {
                    await frameWriter.WriteAsync(first, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await frameWriter.WriteBatchAsync(first, second, cancellationToken).ConfigureAwait(false);
                }

                if (_outputPump.IsCompleted)
                {
                    await ThrowForOutputPumpCompletionAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                if (_outputPump.IsCompleted)
                {
                    await ThrowForOutputPumpCompletionAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
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
        MarkProtocolTerminal();

        Task disposeTask = DisposeCoreAsync(CaptureOperationDrainTask());

        _disposeTask = disposeTask;

        return disposeTask;
    }

    private async ValueTask WriteHandshakeChallengeCoreAsync(ReadOnlyMemory<byte> encryptedChallenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Memory<byte> destination = _outputPipe.Writer.GetMemory(encryptedChallenge.Length);

        encryptedChallenge.Span.CopyTo(destination.Span);
        _outputPipe.Writer.Advance(encryptedChallenge.Length);

        FlushResult flush = await _outputPipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (flush.IsCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException("The game output pipeline canceled the handshake challenge flush.");
        }

        if (flush.IsCompleted)
        {
            await ThrowForOutputPumpCompletionAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeCoreAsync(Task operationsDrained)
    {
        List<Exception>? cleanupExceptions = null;

        try
        {
            await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            _outputPipe.Writer.CancelPendingFlush();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            _inputPipe.Reader.CancelPendingRead();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        await operationsDrained.ConfigureAwait(false);

        try
        {
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            await _inputPipe.Reader.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        if (await ObservePumpShutdownAsync(_inputPump).ConfigureAwait(false) is { } inputFailure &&
            Volatile.Read(ref _inputPumpFailureObserved) == 0)
        {
            (cleanupExceptions ??= []).Add(inputFailure);
        }

        if (await ObservePumpShutdownAsync(_outputPump).ConfigureAwait(false) is { } outputFailure &&
            Volatile.Read(ref _outputPumpFailureObserved) == 0)
        {
            (cleanupExceptions ??= []).Add(outputFailure);
        }

        try
        {
            _frameReader?.Dispose();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            _sessionCipher?.Dispose();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        try
        {
            _lifetimeCancellation.Dispose();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        if (cleanupExceptions is not null)
        {
            throw new AggregateException("One or more game connection session resources failed to dispose.", cleanupExceptions);
        }
    }

    private void EnterOperation()
    {
        lock (_operationGate)
        {
            ThrowIfDisposed();
            _activeOperationCount++;
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource? operationsDrained = null;

        lock (_operationGate)
        {
            _activeOperationCount--;

            if (_activeOperationCount == 0)
            {
                operationsDrained = _operationsDrained;
            }
        }

        operationsDrained?.TrySetResult();
    }

    private Task CaptureOperationDrainTask()
    {
        lock (_operationGate)
        {
            if (_activeOperationCount == 0)
            {
                return Task.CompletedTask;
            }

            return (_operationsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void EnterChallengeWrite()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();
            _handshake.EnterChallengeWrite();
        }
    }

    private void CompleteChallengeWrite()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();
            _handshake.CompleteChallengeWrite();
        }
    }

    private void EnterKeyExchangeRead()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();
            _handshake.EnterKeyExchangeRead();
        }
    }

    private void CompleteKeyExchangeRead()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();
            _handshake.CompleteKeyExchangeRead();
        }
    }

    private void ResetKeyExchangeReadAfterCancellation()
    {
        lock (_protocolGate)
        {
            if (_handshake.CanResetKeyExchangeRead && Volatile.Read(ref _disposeState) == 0)
            {
                _handshake.ResetKeyExchangeRead();
            }
        }
    }

    private GameSecuredFrameReader GetSecuredFrameReader()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (!_handshake.IsSecured)
            {
                throw new InvalidOperationException("The GameServer connection has not completed its secure handshake.");
            }

            return _frameReader ?? throw new InvalidOperationException("The secured GameServer frame reader is unavailable.");
        }
    }

    private GameSecuredFrameWriter GetSecuredFrameWriter()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (!_handshake.IsSecured)
            {
                throw new InvalidOperationException("The GameServer connection has not completed its secure handshake.");
            }

            return _frameWriter ?? throw new InvalidOperationException("The secured GameServer frame writer is unavailable.");
        }
    }

    private void MarkProtocolTerminal()
    {
        lock (_protocolGate)
        {
            _handshake.MarkTerminal();
        }
    }

    private static Pipe CreateInputPipe()
    {
        return new Pipe(new PipeOptions(pauseWriterThreshold: GameWireProtocol.MaximumWireFrameLength, resumeWriterThreshold: GameWireProtocol.MaximumWireFrameLength / 2,
            minimumSegmentSize: GameWireProtocol.MaximumWireFrameLength, useSynchronizationContext: false));
    }

    private static Pipe CreateOutputPipe()
    {
        return new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1,
            minimumSegmentSize: GameWireProtocol.MaximumWireFrameLength, useSynchronizationContext: false));
    }

    private async Task ThrowIfInputPumpFaultedAsync()
    {
        if (_inputPump.IsFaulted)
        {
            Volatile.Write(ref _inputPumpFailureObserved, 1);
            await _inputPump.ConfigureAwait(false);
        }
    }

    private async Task ThrowForOutputPumpCompletionAsync()
    {
        if (_outputPump.IsFaulted)
        {
            Volatile.Write(ref _outputPumpFailureObserved, 1);
        }
        await _outputPump.ConfigureAwait(false);

        throw new EndOfStreamException("The GameServer connection output pump completed before the pending write.");
    }

    private static AggregateException CreateOpenFailure(Exception openException, Exception cleanupException)
    {
        List<Exception> failures = [openException];

        if (cleanupException is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupException);
        }

        return new AggregateException("Failed to open the GameServer connection session and cleanup also failed.", failures);
    }

    private async Task<Exception?> ObservePumpShutdownAsync(Task pump)
    {
        try
        {
            await pump.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException exception) when (
            _lifetimeCancellation.IsCancellationRequested &&
            exception.CancellationToken == _lifetimeCancellation.Token)
        {
            return null;
        }
        catch (Exception exception) when (IsPeerDisconnect(exception))
        {
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static bool IsPeerDisconnect(Exception exception)
    {
        SocketException? socket = exception as SocketException ??
            (exception as IOException)?.InnerException as SocketException;
        return socket?.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
    }
}
