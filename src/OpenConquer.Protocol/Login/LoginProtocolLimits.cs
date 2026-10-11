namespace OpenConquer.Protocol.Login;

/// <summary>
/// Wire limits for the account login protocol.
/// </summary>
public static class LoginProtocolLimits
{
    /// <summary>
    /// Maximum complete frame length across the account login packet set.
    /// </summary>
    /// <remarks>
    /// Packet 1084 is the largest login variant at 524 bytes. This bounds the
    /// login packet set; it is not the transport's absolute frame capacity.
    /// </remarks>
    public const int MaximumFrameLength = 524;
}
