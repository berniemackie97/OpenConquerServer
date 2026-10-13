using System.Net;
using System.Net.Sockets;

namespace OpenConquer.Domain.Realms;

public sealed class RealmRegistration
{
    public RealmRegistration(RealmId id, ProtocolServerName protocolName, IPAddress publicGameAddress, ushort publicGamePort, RealmAvailability availability)
    {
        if (id.Value == 0)
        {
            throw new ArgumentException("A realm requires a nonzero identity.", nameof(id));
        }

        if (string.IsNullOrEmpty(protocolName.Value))
        {
            throw new ArgumentException("A realm requires a validated protocol name.", nameof(protocolName));
        }

        ArgumentNullException.ThrowIfNull(publicGameAddress);

        if (publicGameAddress.AddressFamily != AddressFamily.InterNetwork || publicGameAddress.Equals(IPAddress.Any) || publicGameAddress.Equals(IPAddress.Broadcast))
        {
            throw new ArgumentException("The public GameServer endpoint must be a concrete IPv4 address.", nameof(publicGameAddress));
        }

        ArgumentOutOfRangeException.ThrowIfZero(publicGamePort);

        if (availability is not (RealmAvailability.Enabled or RealmAvailability.Maintenance or RealmAvailability.Disabled))
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }

        Id = id;
        ProtocolName = protocolName;
        PublicGameAddress = publicGameAddress;
        PublicGamePort = publicGamePort;
        Availability = availability;
    }

    public RealmId Id { get; }
    public ProtocolServerName ProtocolName { get; }
    public IPAddress PublicGameAddress { get; }
    public ushort PublicGamePort { get; }
    public RealmAvailability Availability { get; }
}
