using System.Net;

namespace OpenConquer.AccountServer.Login.Workers;

internal readonly record struct LoginConnectionProcessingTimeout(int WorkerIndex, EndPoint LocalEndPoint, EndPoint RemoteEndPoint, TimeSpan ConnectionTimeout);
