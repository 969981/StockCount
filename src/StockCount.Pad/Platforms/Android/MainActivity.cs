using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using StockCount.Pad.Services;

namespace StockCount.Pad;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true, // Launcher activity must be explicit for Android 12+.
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private BarcodeReceiver? _receiver;

    protected override void OnResume()
    {
        base.OnResume();
        ScanFeed.ScannerConfigurationChanged -= OnScannerConfigurationChanged;
        ScanFeed.ScannerConfigurationChanged += OnScannerConfigurationChanged;
        RegisterScannerReceiver();
    }

    protected override void OnPause()
    {
        ScanFeed.ScannerConfigurationChanged -= OnScannerConfigurationChanged;
        UnregisterScannerReceiver();
        base.OnPause();
    }

    private void OnScannerConfigurationChanged()
    {
        // Apply a new vendor Action/Extra without requiring an APK reinstall or restart.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
            RegisterScannerReceiver);
    }

    private void RegisterScannerReceiver()
    {
        UnregisterScannerReceiver();
        var receiver = new BarcodeReceiver();
        var filter = new IntentFilter(ScanFeed.AndroidBroadcastAction);
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                RegisterReceiver(receiver, filter, ReceiverFlags.Exported);
            else
                RegisterReceiver(receiver, filter);
            _receiver = receiver;
        }
        catch
        {
            receiver.Dispose();
            throw;
        }
    }

    private void UnregisterScannerReceiver()
    {
        if (_receiver is null) return;
        UnregisterReceiver(_receiver);
        _receiver.Dispose();
        _receiver = null;
    }

    private sealed class BarcodeReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != ScanFeed.AndroidBroadcastAction) return;
            var text = intent.GetStringExtra(ScanFeed.AndroidBarcodeExtra);
            if (!string.IsNullOrEmpty(text))
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => ScanFeed.OnScan(text));
        }
    }
}
