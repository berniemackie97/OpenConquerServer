using System.IO.Pipelines;
using System.Net;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Connections;

/// <summary>
/// Owns the transport, buffering, handshake transition, cipher, and secured I/O state for one GameServer connection.
/// </summary>
internal sealed class GameConnectionSession : IAsyncDisposable
{
    private const int ProtocolStateBootstrap = 0;
    private const int ProtocolStateSecured = 1;
    private const int ProtocolStateTerminal = 2;

    private const int ChallengeStateNotStarted = 0;
    private const int ChallengeStateActive = 1;
    private const int ChallengeStateCompleted = 2;

    private const int KeyExchangeStateNotStarted = 0;
    private const int KeyExchangeStateActive = 1;
    private const int KeyExchangeStateCompleted = 2;

    private readonly ITransportConnection _connection;
    private readonly Pipe _inputPipe;
    private readonly Pipe _outputPipe;
    private readonly CancellationTokenSource _lifetimeCancellation;
    private readonly GameClientKeyExchangeResponseReader _keyExchangeResponseReader;
    private readonly Task _inputPump;
    private readonly Task _outputPump;

    private readonly Lock _protocolGate = new();
    private readonly Lock _operationGate = new();
    private readonly Lock _disposeGate = new();

    private GameSessionCipher? _sessionCipher;
    private GameSecuredFrameReader? _frameReader;
    private GameSecuredFrameWriter? _frameWriter;
    private TaskCompletionSource? _operationsDrained;
    private Task? _disposeTask;

    private int _protocolState = ProtocolStateBootstrap;
    private int _challengeState = ChallengeStateNotStarted;
    private int _keyExchangeState = KeyExchangeStateNotStarted;
    private int _activeOperationCount;
    private int _disposeState;

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
    /// Takes ownership of <paramref name="connection"/> and starts the duplex transport pumps.
    /// </summary>
    /// <remarks>
    /// After argument validation, ownership of the connection transfers to this operation.
    /// If opening fails, the connection and any created session resources are disposed before the failure is propagated.
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
    /// Sends the one bootstrap-encrypted Diffie-Hellman challenge before secured framing begins.
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
    /// Reads and validates the one bootstrap-encrypted client Diffie-Hellman response.
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

                if (_inputPump.IsFaulted)
                {
                    await _inputPump.ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Transfers ownership of the negotiated session cipher and transitions this connection exactly once to secured framing.
    /// </summary>
    public void CompleteHandshake(GameSessionCipher sessionCipher)
    {
        ArgumentNullException.ThrowIfNull(sessionCipher);

        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (_protocolState != ProtocolStateBootstrap)
            {
                throw new InvalidOperationException("The GameServer connection is not awaiting handshake completion.");
            }

            if (_challengeState != ChallengeStateCompleted || _keyExchangeState != KeyExchangeStateCompleted)
            {
                throw new InvalidOperationException("The GameServer handshake cannot complete before its challenge and client key exchange have completed.");
            }

            GameSecuredFrameReader? frameReader = null;

            try
            {
                frameReader = new GameSecuredFrameReader(_inputPipe.Reader, sessionCipher);
                GameSecuredFrameWriter frameWriter = new(_outputPipe.Writer, sessionCipher);

                _sessionCipher = sessionCipher;
                _frameReader = frameReader;
                _frameWriter = frameWriter;
                _protocolState = ProtocolStateSecured;
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
                if (_inputPump.IsFaulted)
                {
                    await _inputPump.ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        EnterOperation();

        try
        {
            GameSecuredFrameWriter frameWriter = GetSecuredFrameWriter();

            try
            {
                await frameWriter.WriteAsync(packet, cancellationToken).ConfigureAwait(false);

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

        await ObservePumpShutdownAsync(_inputPump).ConfigureAwait(false);
        await ObservePumpShutdownAsync(_outputPump).ConfigureAwait(false);

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

            if (_protocolState != ProtocolStateBootstrap)
            {
                throw new InvalidOperationException("The GameServer connection is not in bootstrap handshake state.");
            }

            if (_challengeState != ChallengeStateNotStarted)
            {
                throw new InvalidOperationException("The GameServer handshake challenge may only be sent once.");
            }

            _challengeState = ChallengeStateActive;
        }
    }

    private void CompleteChallengeWrite()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (_protocolState != ProtocolStateBootstrap || _challengeState != ChallengeStateActive)
            {
                throw new InvalidOperationException("The GameServer handshake challenge state changed unexpectedly while it was being sent.");
            }

            _challengeState = ChallengeStateCompleted;
        }
    }

    private void EnterKeyExchangeRead()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (_protocolState != ProtocolStateBootstrap)
            {
                throw new InvalidOperationException("The GameServer connection is not in bootstrap handshake state.");
            }

            if (_challengeState != ChallengeStateCompleted)
            {
                throw new InvalidOperationException("The client key-exchange response cannot be read before the GameServer handshake challenge has been sent.");
            }

            if (_keyExchangeState != KeyExchangeStateNotStarted)
            {
                throw new InvalidOperationException("The client key-exchange response may only be read once at a time.");
            }

            _keyExchangeState = KeyExchangeStateActive;
        }
    }

    private void CompleteKeyExchangeRead()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (_protocolState != ProtocolStateBootstrap || _keyExchangeState != KeyExchangeStateActive)
            {
                throw new InvalidOperationException("The GameServer client key-exchange state changed unexpectedly while it was being read.");
            }

            _keyExchangeState = KeyExchangeStateCompleted;
        }
    }

    private void ResetKeyExchangeReadAfterCancellation()
    {
        lock (_protocolGate)
        {
            if (_protocolState == ProtocolStateBootstrap && _keyExchangeState == KeyExchangeStateActive && Volatile.Read(ref _disposeState) == 0)
            {
                _keyExchangeState = KeyExchangeStateNotStarted;
            }
        }
    }

    private GameSecuredFrameReader GetSecuredFrameReader()
    {
        lock (_protocolGate)
        {
            ThrowIfDisposed();

            if (_protocolState != ProtocolStateSecured)
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

            if (_protocolState != ProtocolStateSecured)
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
            _protocolState = ProtocolStateTerminal;
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

    private async Task ThrowForOutputPumpCompletionAsync()
    {
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

    private static async Task ObservePumpShutdownAsync(Task pump)
    {
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch
        {
            // Session disposal intentionally aborts outstanding transport I/O.
            // Awaiting here observes the terminal pump state so no background
            // task escapes the session lifetime.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
    }
}
