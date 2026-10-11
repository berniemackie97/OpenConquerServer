namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Decoded post-authentication MAC address report (packet 1100).
/// </summary>
/// <remarks>
/// The reported hardware address is client-controlled telemetry and must not be
/// treated as authentication proof.
/// </remarks>
public sealed class LoginAccountMacAddressReport
{
    internal LoginAccountMacAddressReport(uint sessionUid, string macAddress)
    {
        ArgumentNullException.ThrowIfNull(macAddress);

        SessionUid = sessionUid;
        MacAddress = macAddress;
    }

    public uint SessionUid { get; }

    /// <summary>
    /// Gets the client-reported hardware address text. The client normally sends twelve uppercase hexadecimal characters or an
    /// empty value when hardware discovery fails.
    /// </summary>
    public string MacAddress { get; }
}
