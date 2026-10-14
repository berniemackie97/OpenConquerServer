namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Failure codes carried in the second UInt32 field of a failed
/// account authentication response (packet 1055).
/// </summary>
/// <remarks>
/// The client shows the text of its <c>STR_CONNECT_ERROR_MSG_TYPE_%03u</c>
/// string-table entry for the code; each member documents that text.
/// </remarks>
public enum LoginAccountAuthenticationFailureCode : uint
{
    /// <summary>"Invalid Account ID or Password"</summary>
    InvalidCredentials = 1,

    /// <summary>"The Server is down."</summary>
    ServerDown = 10,

    /// <summary>"Please try again later."; TQ's refusal while the account is still playing in another realm.</summary>
    AlreadyOnline = 11,

    /// <summary>"This account has been banned."</summary>
    Banned = 12,

    /// <summary>"Server is busy."</summary>
    ServerBusy = 20,

    /// <summary>"Server is busy. Please try again, later."; TQ's server-full refusal.</summary>
    ServerFull = 21,

    /// <summary>"Invalid Account ID"</summary>
    InvalidAccount = 57,

    /// <summary>"Server Locked"</summary>
    ServerLocked = 70,
}
