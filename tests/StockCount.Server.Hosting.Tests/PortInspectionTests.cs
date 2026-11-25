using System.Net;
using System.Net.Sockets;
using StockCount.Server.Hosting;
using Xunit;

namespace StockCount.Server.Hosting.Tests;

public sealed class PortInspectionTests
{
    [Fact]
    public void WindowsListenerTableIdentifiesActualProcessForIpv4AndIpv6()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var rows = PortInspection.Read(port);
        Assert.Contains(rows, row => row.Port == port && row.ProcessId == Environment.ProcessId);
        if (!Socket.OSSupportsIPv6) return;
        using var v6 = new TcpListener(IPAddress.IPv6Loopback, 0);
        v6.Start();
        var port6 = ((IPEndPoint)v6.LocalEndpoint).Port;
        Assert.Contains(PortInspection.Read(port6), row => row.Port == port6 && row.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void OnlyConfirmedStockCountWithBothPortsCanBeStopped()
    {
        var api = new PortOwner(5080, 123, "StockCount.Api", @"C:\StockCount\StockCount.Api.exe", 100);
        var management = api with { Port = 5081 };
        Assert.Equal(api, PortInspection.ConfirmedExternalApi(new[] { api, management }, 5080, true));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api, management }, 5080, false));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api }, 5080, true));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api, management with { ProcessId = 456 } }, 5080, true));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api, management with { StartedUtcTicks = 200 } }, 5080, true));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api, management with { Path = null } }, 5080, true));
        Assert.Null(PortInspection.ConfirmedExternalApi(new[] { api, management with { Name = "Other" } }, 5080, true));
    }
}
