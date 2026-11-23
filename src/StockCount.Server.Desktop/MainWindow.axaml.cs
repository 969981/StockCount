using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StockCount.Server.Desktop.ViewModels;

namespace StockCount.Server.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            _timer.Tick += async (_, _) =>
            {
                await vm.RefreshStatusCommand.ExecuteAsync(null);
                await vm.RefreshLogsCommand.ExecuteAsync(null);
            };
            _timer.Start();
            _ = vm.RefreshStatusCommand.ExecuteAsync(null);
            _ = vm.RefreshLogsCommand.ExecuteAsync(null);
        };
        Closing += async (_, args) =>
        {
            if (_shutdownComplete) return;
            args.Cancel = true;
            _timer.Stop();
            try
            {
                if (DataContext is MainViewModel vm) await vm.CloseAsync();
            }
            finally
            {
                _shutdownComplete = true;
                Close();
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void CopyDeviceKey(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            if (Clipboard is null) throw new InvalidOperationException("系统剪贴板不可用。");
            await Clipboard.SetTextAsync(vm.GetDeviceKeyForClipboard());
            vm.Message = "设备访问密钥已复制，请只通过可信渠道提供给 PDA。";
        }
        catch (Exception ex) { vm.Message = "复制失败：" + ex.Message; }
    }
}
