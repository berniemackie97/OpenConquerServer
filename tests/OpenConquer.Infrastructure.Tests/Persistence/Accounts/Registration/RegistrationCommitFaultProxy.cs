using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Registration;

/// <summary>Drops one plaintext MySQL COMMIT exchange against the disposable test database.</summary>
internal sealed class RegistrationCommitFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly string _server;
    private readonly int _port;
    private readonly bool _forwardCommit;
    private readonly bool _rejectRecovery;
    private readonly Task _accept;
    private int _faultUsed;
    private int _insertCount;
    private int _commitAcknowledged;

    public RegistrationCommitFaultProxy(string server, int port, bool forwardCommit, bool rejectRecovery = false)
    {
        _server = server;
        _port = port;
        _forwardCommit = forwardCommit;
        _rejectRecovery = rejectRecovery;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    public int Port { get; }
    public int InsertCount => Volatile.Read(ref _insertCount);
    public bool CommitAcknowledged => Volatile.Read(ref _commitAcknowledged) != 0;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (TcpClient upstream = new())
        using (CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            if (_rejectRecovery && Volatile.Read(ref _faultUsed) != 0)
            {
                return;
            }

            await upstream.ConnectAsync(_server, _port, connection.Token);
            int awaitingCommitReply = 0;
            Task requests = PumpAsync(client.GetStream(), upstream.GetStream(), fromClient: true);
            Task responses = PumpAsync(upstream.GetStream(), client.GetStream(), fromClient: false);
            await Task.WhenAny(requests, responses);
            await connection.CancelAsync();
            await Task.WhenAll(requests, responses);

            async Task PumpAsync(NetworkStream source, NetworkStream destination, bool fromClient)
            {
                try
                {
                    while (true)
                    {
                        byte[] header = new byte[4];
                        await source.ReadExactlyAsync(header, connection.Token);
                        int length = BinaryPrimitives.ReadInt32LittleEndian(header) & 0x00ff_ffff;
                        byte[] payload = new byte[length];
                        await source.ReadExactlyAsync(payload, connection.Token);
                        if (fromClient && payload.Length > 1 && payload[0] == 3)
                        {
                            // MySQL 8 negotiates query attributes: zero parameters, one parameter set.
                            int sqlOffset = payload.Length >= 3 && payload[1] == 0 && payload[2] == 1 ? 3 : 1;
                            if (payload.AsSpan(sqlOffset).StartsWith("INSERT INTO `accounts`"u8))
                            {
                                Interlocked.Increment(ref _insertCount);
                            }

                            if (Encoding.UTF8.GetString(payload.AsSpan(sqlOffset)).Trim().TrimEnd(';').Equals("COMMIT", StringComparison.OrdinalIgnoreCase) && Interlocked.CompareExchange(ref _faultUsed, 1, 0) == 0)
                            {
                                if (!_forwardCommit)
                                {
                                    return;
                                }

                                Volatile.Write(ref awaitingCommitReply, 1);
                            }
                        }
                        else if (!fromClient && Volatile.Read(ref awaitingCommitReply) != 0)
                        {
                            Assert.NotEmpty(payload);
                            Assert.Equal(0, payload[0]); // MySQL confirmed the commit; hide its OK packet from the store.
                            Interlocked.Exchange(ref _commitAcknowledged, 1);
                            return;
                        }

                        await destination.WriteAsync(header, connection.Token);
                        await destination.WriteAsync(payload, connection.Token);
                    }
                }
                catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }
    }
}
