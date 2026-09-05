namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Represents the decoded fields of the client -> AccountServer post authentication MAC address report packet 1100.
/// </summary>
public sealed class LoginAccountMacAddressReport
{
    internal LoginAccountMacAddressReport(uint sessionUid, string macAddress)
    {
        ArgumentNullException.ThrowIfNull(macAddress);

        SessionUid = sessionUid;
        MacAddress = macAddress;
    }

    public uint SessionUid { get; }
    public string MacAddress { get; }
}
