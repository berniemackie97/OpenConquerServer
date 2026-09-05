using System.IO.Pipelines;
using System.Net;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Protocol.Login;
using OpenConquer.Protocol.Login.Cryptography;
using OpenConquer.Protocol.Login.Packets;
using OpenConquer.Protocol.Packets;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Login.Connections;

/// <summary>
/// Owns the transport, buffering, login cipher, and framed I/O state for one account login connection.
/// </summary>
internal sealed class LoginConnectionSession : IAsyncDisposable
{
    private readonly ITransportConnection _connection;
    private readonly Pipe _inputPipe;
    private readonly Pipe _outputPipe;
    private readonly CancellationTokenSource _lifetimeCancellation;

    private readonly LoginFrameReader _frameReader;
    private readonly LoginFrameWriter _frameWriter;

    private readonly Task _inputPump;
    private readonly Task _outputPump;

    private readonly Lock _disposeGate = new();

    private Task? _disposeTask;
    private int _disposeState;

    private LoginConnectionSession(ITransportConnection connection)
    {
        _connection = connection;

        _lifetimeCancellation = new CancellationTokenSource();

        _inputPipe = CreateInputPipe();
        _outputPipe = CreateOutputPipe();

        LoginStreamCipher cipher = new();

        _frameReader = new LoginFrameReader(_inputPipe.Reader, cipher);
        _frameWriter = new LoginFrameWriter(_outputPipe.Writer, cipher);

        _inputPump = TransportConnectionInput.PumpAsync(connection, _inputPipe.Writer, _lifetimeCancellation.Token);
        _outputPump = TransportConnectionOutput.PumpAsync(connection, _outputPipe.Reader, _lifetimeCancellation.Token);
    }

    public uint LoginSeed { get; private set; }

    public EndPoint LocalEndPoint => _connection.LocalEndPoint;
    public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;

    public static async ValueTask<LoginConnectionSession> OpenAsync(ITransportConnection connection, ILoginSeedGenerator seedGenerator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(seedGenerator);

        LoginConnectionSession? session = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            session = new LoginConnectionSession(connection);

            uint seed = seedGenerator.GenerateSeed();

            session.LoginSeed = seed;

            await session.SendInitialSeedAsync(seed, cancellationToken).ConfigureAwait(false);

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

    public async ValueTask<LoginInboundFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            return await _frameReader.ReadAsync(cancellationToken).ConfigureAwait(false);
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

    public async ValueTask WriteAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        ThrowIfDisposed();

        try
        {
            await _frameWriter.WriteAsync(packet, cancellationToken).ConfigureAwait(false);

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

    private async Task SendInitialSeedAsync(uint seed, CancellationToken cancellationToken)
    {
        Task writeTask = WriteAsync(new LoginSeedPacket(seed), cancellationToken).AsTask();

        try
        {
            Task completedTask = await Task.WhenAny(writeTask, _inputPump).ConfigureAwait(false);
            if (completedTask == _inputPump)
            {
                await ThrowForInputPumpCompletionDuringOpenAsync().ConfigureAwait(false);
            }

            await writeTask.ConfigureAwait(false);

            if (_inputPump.IsCompleted)
            {
                await ThrowForInputPumpCompletionDuringOpenAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            _outputPipe.Writer.CancelPendingFlush();
            try
            {
                await writeTask.ConfigureAwait(false);
            }
            catch
            {
                // Preserve the opening failure after observing the aborted seed write.
            }

            throw;
        }
    }

    private async Task DisposeCoreAsync()
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
            _lifetimeCancellation.Dispose();
        }
        catch (Exception exception)
        {
            (cleanupExceptions ??= []).Add(exception);
        }

        if (cleanupExceptions is not null)
        {
            throw new AggregateException("One or more login connection session resources failed to dispose.", cleanupExceptions);
        }
    }

    private static Pipe CreateInputPipe()
    {
        return new Pipe(new PipeOptions(pauseWriterThreshold: LoginProtocolLimits.MaximumFrameLength, resumeWriterThreshold: LoginProtocolLimits.MaximumFrameLength / 2,
                minimumSegmentSize: LoginProtocolLimits.MaximumFrameLength, useSynchronizationContext: false));
    }

    private static Pipe CreateOutputPipe()
    {
        return new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1, minimumSegmentSize: LoginProtocolLimits.MaximumFrameLength, useSynchronizationContext: false));
    }

    private async Task ThrowForInputPumpCompletionDuringOpenAsync()
    {
        await _inputPump.ConfigureAwait(false);

        throw new EndOfStreamException("The login connection input pump completed before the initial handshake finished.");
    }

    private async Task ThrowForOutputPumpCompletionAsync()
    {
        await _outputPump.ConfigureAwait(false);

        throw new EndOfStreamException("The login connection output pump completed before the pending frame write.");
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

        return new AggregateException("Failed to open the login connection session and cleanup also failed.", failures);
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
