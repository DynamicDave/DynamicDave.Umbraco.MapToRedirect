using System.Net;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class PrivateNetworkTests
{
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("fd00::1")]
    // IPv6 forms that embed an IPv4 address reach that address through a gateway or a dual stack.
    [InlineData("64:ff9b::a00:5")] // NAT64 of 10.0.0.5
    [InlineData("64:ff9b::7f00:1")] // NAT64 of 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 of 169.254.169.254
    [InlineData("2002:a00:5::1")] // 6to4 of 10.0.0.5
    [InlineData("2002:c0a8:101::")] // 6to4 of 192.168.1.1
    [InlineData("::10.0.0.5")] // IPv4-compatible
    [InlineData("::127.0.0.1")]
    public void Internal_addresses_are_blocked(string address) =>
        Assert.True(PrivateNetwork.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("64:ff9b::5db8:d822")] // NAT64 of 93.184.216.34
    [InlineData("2002:5db8:d822::1")] // 6to4 of 93.184.216.34
    [InlineData("::93.184.216.34")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.False(PrivateNetwork.IsBlocked(IPAddress.Parse(address)));
}
