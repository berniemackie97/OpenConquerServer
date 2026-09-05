namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Describes whether a persisted account may complete password authentication.
/// </summary>
public enum AccountLoginAccess
{
    Denied = 0,
    Allowed = 1,
    Banned = 2,
}
