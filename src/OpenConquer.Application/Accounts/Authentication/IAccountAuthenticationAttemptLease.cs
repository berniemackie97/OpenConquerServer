namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents one admitted account-authentication attempt.
/// </summary>
/// <remarks>
/// Disposing an attempt without completing it abandons the attempt. This allows
/// cancellation and exceptional exits to release protection state without
/// recording a credential success or failure that did not fully complete.
/// </remarks>
public interface IAccountAuthenticationAttemptLease : IDisposable
{
    /// <summary>
    /// Completes the attempt and reports whether the supplied credentials were
    /// accepted.
    /// </summary>
    /// <remarks>
    /// A valid password for an account that is subsequently denied login or is
    /// banned still represents accepted credentials for brute-force protection
    /// purposes.
    /// </remarks>
    void Complete(bool credentialsAccepted);
}
