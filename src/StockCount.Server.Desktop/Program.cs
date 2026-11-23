using Avalonia;

namespace StockCount.Server.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // This prevents two Avalonia managers owning an API child concurrently.
        // A pre-existing Tauri or CLI listener is also checked on StartAsync.
        using var mutex = new Mutex(true, @"Local\Dreamer.StockCount.ServerDesktop", out var first);
        if (!first) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
