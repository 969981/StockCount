using Avalonia;
using System.Runtime.InteropServices;

namespace StockCount.Server.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // This prevents two Avalonia managers owning an API child concurrently.
        // A pre-existing Tauri or CLI listener is also checked on StartAsync.
        using var mutex = new Mutex(true, @"Local\Dreamer.StockCount.ServerDesktop", out var first);
        if (!first)
        {
            MessageBoxW(IntPtr.Zero, "StockCount 服务管理器已在当前 Windows 会话运行，请返回已打开的窗口。",
                "StockCount · 已运行", 0x40);
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
