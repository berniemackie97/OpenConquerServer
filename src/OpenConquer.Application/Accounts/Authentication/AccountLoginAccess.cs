namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Describes whether a persisted account may complete password authentication.
/// </summary>
/// <remarks>
/// This is intentionally independent of gameplay or staff roles. Persistence
/// adapters may map legacy permission/status representations into this narrow
/// authentication decision.
/// </remarks>
public enum AccountLoginAccess
{
    Denied = 0,
    Allowed = 1,
    Banned = 2,
}
