namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

/// <summary>
/// Represents one admitted AccountServer login connection.
/// </summary>
/// <remarks>
/// Disposing the lease releases the connection's per-source concurrency admission.
/// </remarks>
public interface IAccountLoginConnectionLease : IDisposable;
