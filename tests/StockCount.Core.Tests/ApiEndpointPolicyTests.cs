using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ApiEndpointPolicyTests
{
    [Theory]
    [InlineData("192.168.1.100:5080", "http://192.168.1.100:5080")]
    [InlineData(" 10.0.0.2:5080/ ", "http://10.0.0.2:5080")]
    [InlineData("https://example.com:5080", "https://example.com:5080")]
    public void NormalizesLanAddress(string input, string expected)
    {
        Assert.True(ApiEndpointPolicy.TryNormalize(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("8.8.8.8:5080")]
    [InlineData("example.com:5080")]
    [InlineData("192.168.1.100:5080/api")]
    [InlineData("ftp://192.168.1.100")]
    public void RejectsUnsafeShorthand(string input) =>
        Assert.False(ApiEndpointPolicy.TryNormalize(input, out _));

    [Theory]
    [InlineData("192.168.1.100", true)]
    [InlineData("::ffff:10.1.2.3", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("2001:db8::1", false)]
    public void ClassifiesDirectPrivatePeers(string input, bool allowed) =>
        Assert.Equal(allowed, ApiEndpointPolicy.IsPrivateLanAddress(System.Net.IPAddress.Parse(input)));

    [Theory]
    [InlineData("http://192.168.1.100:5080")]
    [InlineData("http://10.0.2.2:5080")]
    [InlineData("http://172.16.0.20:5080")]
    [InlineData("http://172.31.255.254:5080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://localhost:5080")]
    [InlineData("https://example.com:5080")]
    public void AcceptsPrivateNetworkHttpOrHttps(string url) =>
        Assert.True(ApiEndpointPolicy.IsAllowed(new Uri(url)));

    [Theory]
    [InlineData("http://8.8.8.8:5080")]
    [InlineData("http://172.32.0.1:5080")]
    [InlineData("http://192.169.1.2:5080")]
    [InlineData("http://example.com:5080")]
    [InlineData("ftp://192.168.1.100")]
    [InlineData("http://user:pass@192.168.1.100:5080")]
    [InlineData("http://192.168.1.100:5080/api")]
    [InlineData("http://192.168.1.100:5080?key=secret")]
    public void RejectsNonLanHttpAndUnsafeUrlParts(string url) =>
        Assert.False(ApiEndpointPolicy.IsAllowed(new Uri(url)));
}
