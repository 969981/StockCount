using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StockCount.Server.Desktop.ViewModels;
using StockCount.Server.Hosting;

namespace StockCount.Server.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsStore(approvedFieldWrites: true);
            var vault = new WindowsCredentialVault();
            var runtime = new ServerRuntime(settings, vault);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(settings, vault, runtime, new LogReader(settings))
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
