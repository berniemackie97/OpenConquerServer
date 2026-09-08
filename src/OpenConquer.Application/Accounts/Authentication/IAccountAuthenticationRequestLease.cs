namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents one admitted account-authentication request.
/// </summary>
/// <remarks>
/// Disposing the lease releases the request's concurrency admission. Request-rate
/// admission is consumed when the lease is created and is not restored on disposal.
/// </remarks>
public interface IAccountAuthenticationRequestLease : IDisposable;
