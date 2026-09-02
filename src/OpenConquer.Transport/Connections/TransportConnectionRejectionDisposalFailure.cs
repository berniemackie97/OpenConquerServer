using System.Net;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Describes a failure to dispose a transport connection that was rejected
/// because bounded admission capacity was exhausted.
/// </summary>
public readonly record struct TransportConnectionRejectionDisposalFailure(EndPoint LocalEndPoint, EndPoint RemoteEndPoint, Exception Exception);
