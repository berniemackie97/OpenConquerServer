using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace OpenConquer.Infrastructure.Security;

internal readonly record struct RemoteAddressSourceKey(bool IsIpv6, ulong Network)
{
    public static RemoteAddressSourceKey Create(IPAddress remoteAddress)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        Span<byte> bytes = stackalloc byte[16];

        if (!remoteAddress.TryWriteBytes(bytes, out int bytesWritten))
        {
            throw new InvalidOperationException("The remote IP address could not be represented as bytes.");
        }

        if (remoteAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytesWritten != 4)
            {
                throw new InvalidOperationException("An IPv4 address produced an unexpected byte length.");
            }

            return new RemoteAddressSourceKey(IsIpv6: false, BinaryPrimitives.ReadUInt32BigEndian(bytes));
        }

        if (remoteAddress.AddressFamily != AddressFamily.InterNetworkV6 || bytesWritten != 16)
        {
            throw new ArgumentException("Only IPv4 and IPv6 remote addresses are supported.", nameof(remoteAddress));
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            return new RemoteAddressSourceKey(IsIpv6: false, BinaryPrimitives.ReadUInt32BigEndian(bytes[12..]));
        }

        return new RemoteAddressSourceKey(IsIpv6: true, BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }
}
