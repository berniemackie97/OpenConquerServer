namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Result codes of the OpenConquer account-registration response (packet 0xF002).
/// </summary>
public enum LoginAccountRegistrationResultCode : uint
{
    /// <summary>The account was created.</summary>
    Success = 0,

    /// <summary>The request was malformed or the name or password breaks registration policy.</summary>
    InvalidRequest = 1,

    /// <summary>An account with that name already exists.</summary>
    AccountNameTaken = 2,

    /// <summary>The server could not confirm that the account was created.</summary>
    Failed = 3,

    /// <summary>Registration is disabled or this source has made too many requests; retry later.</summary>
    Unavailable = 4,
}
