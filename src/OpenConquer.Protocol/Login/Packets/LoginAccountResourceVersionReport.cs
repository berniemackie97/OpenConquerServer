namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Represents the decoded fields of the client -> AccountServer resource version report packet 1052.
/// </summary>
public sealed class LoginAccountResourceVersionReport
{
    internal LoginAccountResourceVersionReport(uint sessionUid, int resourceVersion, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);

        SessionUid = sessionUid;
        ResourceVersion = resourceVersion;
        ResourceName = resourceName;
    }

    public uint SessionUid { get; }
    public int ResourceVersion { get; }
    public string ResourceName { get; }
}
