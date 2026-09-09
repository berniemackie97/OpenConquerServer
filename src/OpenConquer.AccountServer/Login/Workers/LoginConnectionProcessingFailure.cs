using System.Net;

namespace OpenConquer.AccountServer.Login.Workers;

internal readonly record struct LoginConnectionProcessingFailure(int WorkerIndex, EndPoint LocalEndPoint, EndPoint RemoteEndPoint, Exception Exception);
