using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ApiEndpointPolicyTests
{
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
