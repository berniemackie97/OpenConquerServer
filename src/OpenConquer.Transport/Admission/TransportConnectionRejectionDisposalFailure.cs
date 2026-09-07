using System.Net;

namespace OpenConquer.Transport.Admission;

public readonly record struct TransportConnectionRejectionDisposalFailure(EndPoint LocalEndPoint, EndPoint RemoteEndPoint, Exception Exception);
