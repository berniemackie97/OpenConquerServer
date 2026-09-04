namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Describes the result of verifying a supplied password against a persisted
/// encoded password hash.
/// </summary>
public enum AccountPasswordVerificationStatus
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}
