using System.Net;

namespace OpenConquer.Transport.Connections;

public readonly record struct TransportConnectionRejectionDisposalFailure(EndPoint LocalEndPoint, EndPoint RemoteEndPoint, Exception Exception);
