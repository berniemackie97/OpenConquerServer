namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Represents the decoded fields of the native client-to-AccountServer
/// resource-version report packet 1052.
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

    /// <summary>
    /// Gets the client-reported signed 32-bit resource version.
    /// </summary>
    public int ResourceVersion { get; }

    /// <summary>
    /// Gets the client-reported resource file name.
    /// </summary>
    /// <remarks>
    /// The verified retail client sends <c>res.dat</c>. Whether another value
    /// is accepted is AccountServer policy rather than packet structure.
    /// </remarks>
    public string ResourceName { get; }
}
