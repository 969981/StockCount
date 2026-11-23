using System.Net;
using System.Net.Sockets;

namespace StockCount.Core;

/// <summary>
/// QA: verify trusted LAN HTTP and reject public HTTP endpoints.
/// Allows HTTPS endpoints and HTTP only for local development or private
/// IPv4 LAN addresses. Cleartext device credentials are NEVER allowed to
/// arbitrary public hosts by the PDA client. The server still needs a
/// trusted, isolated LAN, firewall and device access key.
/// </summary>
public static class ApiEndpointPolicy
{
    public static bool IsAllowed(Uri? endpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/")
            return false;

        if (endpoint.Scheme == Uri.UriSchemeHttps) return true;
        if (endpoint.Scheme != Uri.UriSchemeHttp) return false;
        if (endpoint.IsLoopback) return true;
        if (!IPAddress.TryParse(endpoint.Host, out var ip)) return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168);
    }
}
