namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents one admitted account authentication attempt.
/// </summary>
public interface IAccountAuthenticationAttemptLease : IDisposable
{
    /// <summary>
    /// Completes the attempt and reports whether the supplied credentials were accepted.
    /// </summary>
    void Complete(bool credentialsAccepted);
}
