namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Describes the externally usable outcome of an account authentication attempt.
/// </summary>
public enum AccountAuthenticationStatus
{
    Unspecified = 0,
    Success = 1,
    InvalidCredentials = 2,
    Banned = 3,
}
