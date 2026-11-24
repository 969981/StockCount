using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StockCount.Core;

/// <summary>TCP is decisive for API access; ICMP is only a supplementary reachability hint.</summary>
public static class ConnectionDiagnostics
{
    public static string DescribeSocketError(SocketError error) => error switch
    {
        SocketError.ConnectionRefused => "连接被拒绝：目标端口未监听或被防火墙拒绝。",
        SocketError.TimedOut => "连接超时：目标未响应，可能是防火墙、网络隔离或地址错误。",
        SocketError.HostUnreachable => "主机不可达：检查目标 IP、Wi-Fi 和网关。",
        SocketError.NetworkUnreachable or SocketError.NetworkDown => "网络不可达：检查 Wi-Fi 和网络路由。",
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "域名解析失败：检查地址和 DNS。",
        SocketError.AccessDenied => "网络访问被拒绝：检查设备网络权限或系统限制。",
        SocketError.ConnectionReset or SocketError.ConnectionAborted => "连接被中断：检查服务进程及网络。",
        _ => "TCP 连接失败（" + error + "）。"
    };

    public static string DescribeException(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SocketException socket)
                return DescribeSocketError(socket.SocketErrorCode);
        return error switch
        {
            OperationCanceledException => "请求超时，目标在规定时间内未响应。",
            HttpRequestException http when http.HttpRequestError == HttpRequestError.SecureConnectionError =>
                "HTTPS 握手失败：检查证书、协议及端口。",
            HttpRequestException http when http.HttpRequestError == HttpRequestError.NameResolutionError =>
                "域名解析失败：检查地址和 DNS。",
            HttpRequestException http when http.HttpRequestError == HttpRequestError.ConnectionError =>
                "HTTP 连接失败：检查服务监听、端口和防火墙。",
            _ => "连接失败（" + error.GetType().Name + "）。"
        };
    }

    public static async Task<string> ProbeIcmpAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1000).WaitAsync(TimeSpan.FromSeconds(2));
            return reply.Status == IPStatus.Success
                ? $"ICMP Ping：有响应（{reply.RoundtripTime} ms），主机可达。"
                : $"ICMP Ping：无有效响应（{reply.Status}）；可能禁止 Ping，不能单独断定 IP 不通。";
        }
        catch (Exception)
        {
            return "ICMP Ping：当前设备不支持或探测失败，无法单独判断 IP 是否可达。";
        }
    }

    public static async Task<(bool Reachable, string Detail)> ProbeTcpAsync(string host, int port)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(host, port, timeout.Token);
            return (true, $"TCP {host}:{port}：连接成功（{stopwatch.ElapsedMilliseconds} ms），目标 IP/端口可达。");
        }
        catch (Exception error)
        {
            return (false, $"TCP {host}:{port}：{DescribeException(error)}");
        }
    }
}
