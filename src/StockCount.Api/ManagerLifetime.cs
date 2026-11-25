using System.Diagnostics;

namespace StockCount.Api;

/// <summary>Keep a real handle to the verified manager process; PID alone is not identity.</summary>
public sealed class ManagerLifetime : IDisposable
{
    private readonly Process _parent;
    private ManagerLifetime(Process parent) => _parent = parent;

    public static ManagerLifetime? Open(string? pidText, string? ticksText)
    {
        if (string.IsNullOrEmpty(pidText) && string.IsNullOrEmpty(ticksText)) return null;
        if (!int.TryParse(pidText, out var pid) || pid <= 0 || pid == Environment.ProcessId ||
            !long.TryParse(ticksText, out var ticks))
            throw new InvalidOperationException("管理器进程身份参数无效。");
        var parent = Process.GetProcessById(pid);
        try
        {
            _ = parent.Handle;
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != ticks)
                throw new InvalidOperationException("管理器已退出或进程身份已变化，取消启动 API。");
            return new ManagerLifetime(parent);
        }
        catch { parent.Dispose(); throw; }
    }

    public async Task WatchAsync(IHostApplicationLifetime lifetime, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await _parent.WaitForExitAsync(cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("所属管理器已退出，API 正在正常关闭。");
                lifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "管理器进程监控失败，API 将停止以避免残留。");
            lifetime.StopApplication();
        }
    }

    public void Dispose() => _parent.Dispose();
}
