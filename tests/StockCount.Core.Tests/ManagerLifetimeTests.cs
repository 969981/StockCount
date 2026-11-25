using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using StockCount.Api;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ManagerLifetimeTests
{
    [Fact]
    public async Task VerifiedManagerExitRequestsGracefulApplicationShutdown()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60")
            : new ProcessStartInfo("/bin/sleep", "60");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using var parent = Process.Start(info)!;
        try
        {
            var pid = parent.Id.ToString();
            var ticks = parent.StartTime.ToUniversalTime().Ticks;
            Assert.Throws<InvalidOperationException>(() => ManagerLifetime.Open(pid, (ticks + 1).ToString()));
            using var watcher = ManagerLifetime.Open(pid, ticks.ToString())!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var lifetime = new TestLifetime();
            var waiting = watcher.WatchAsync(lifetime, NullLogger.Instance, timeout.Token);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
            parent.Kill();
            await waiting.WaitAsync(timeout.Token);
            Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
        }
        finally
        {
            if (!parent.HasExited) parent.Kill();
            await parent.WaitForExitAsync();
        }
    }

    [Fact]
    public void StandaloneDoesNotRequireManager()
    {
        Assert.Null(ManagerLifetime.Open("", ""));
        Assert.Throws<InvalidOperationException>(() => ManagerLifetime.Open("bad", "123"));
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() => _stopping.Dispose();
    }
}
