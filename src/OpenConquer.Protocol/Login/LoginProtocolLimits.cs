namespace OpenConquer.Protocol.Login;

/// <summary>
/// Defines verified wire limits for the 5517 account-login protocol.
/// </summary>
public static class LoginProtocolLimits
{
    /// <summary>
    /// Maximum complete frame length currently established by verified native
    /// 5517 account-login packet variants.
    /// </summary>
    /// <remarks>
    /// Native packet 1084 is the largest verified account-login variant at
    /// 524 bytes. This is a compatibility boundary for the established login
    /// packet set, not the generic socket transport's absolute frame capacity.
    /// </remarks>
    public const int MaximumFrameLength = 524;
}
