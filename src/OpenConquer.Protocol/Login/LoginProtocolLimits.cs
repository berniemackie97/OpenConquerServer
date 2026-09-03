namespace OpenConquer.Protocol.Login;

/// <summary>
/// Defines verified wire limits for the 5517 account-login protocol.
/// </summary>
public static class LoginProtocolLimits
{
    /// <summary>
    /// Maximum complete account-login frame length accepted by the verified
    /// native protocol.
    /// </summary>
    public const int MaximumFrameLength = 524;
}
