using System.Net;
using System.Net.Sockets;
using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ConnectionDiagnosticsTests
{
    [Theory]
    [InlineData(SocketError.ConnectionRefused, "连接被拒绝")]
    [InlineData(SocketError.TimedOut, "连接超时")]
    [InlineData(SocketError.HostUnreachable, "主机不可达")]
    [InlineData(SocketError.NetworkUnreachable, "网络不可达")]
    [InlineData(SocketError.HostNotFound, "域名解析失败")]
    public void ReportsSpecificSocketErrors(SocketError error, string expected)
    {
        Assert.Contains(expected, ConnectionDiagnostics.DescribeSocketError(error));
        var wrapped = new HttpRequestException("Connection failure", new SocketException((int)error));
        Assert.Contains(expected, ConnectionDiagnostics.DescribeException(wrapped));
    }

    [Fact]
    public void TimeoutIsNotAnUnconditionalHostUnreachableClaim()
    {
        Assert.Contains("超时", ConnectionDiagnostics.DescribeException(new OperationCanceledException()));
        Assert.DoesNotContain("主机不可达", ConnectionDiagnostics.DescribeException(new OperationCanceledException()));
    }

    [Fact]
    public async Task TcpProbeReachesAnActualLoopbackListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var result = await ConnectionDiagnostics.ProbeTcpAsync("127.0.0.1", port);
        Assert.True(result.Reachable, result.Detail);
        Assert.Contains("连接成功", result.Detail);
    }
}
