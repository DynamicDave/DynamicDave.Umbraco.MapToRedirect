using System.Net;
using System.Net.Sockets;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

internal static class PrivateNetwork
{
    /// <summary>
    /// True for addresses the package must not contact on a production server: loopback, private (RFC 1918, ULA),
    /// carrier-grade NAT, link-local (incl. cloud metadata 169.254.169.254), unspecified, multicast and reserved.
    /// The check runs on the resolved address, so DNS names pointing at internal addresses are blocked too.
    /// </summary>
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] is >= 64 and <= 127,
                169 => b[1] == 254,
                172 => b[1] is >= 16 and <= 31,
                192 => b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2),
                198 => b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100),
                203 => b[1] == 0 && b[2] == 113,
                >= 224 => true,
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None)) return true;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal) return true;
            var b = address.GetAddressBytes();
            return b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8; // 2001:db8::/32 documentation
        }

        return true;
    }
}
