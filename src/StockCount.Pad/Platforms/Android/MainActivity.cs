using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using StockCount.Pad.Services;

namespace StockCount.Pad;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private BarcodeReceiver? _receiver;

    protected override void OnResume()
    {
        base.OnResume();
        if (_receiver is not null) return;
        _receiver = new BarcodeReceiver();
        var filter = new IntentFilter("com.service.scanner.data");
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_receiver, filter, ReceiverFlags.Exported);
        else
            RegisterReceiver(_receiver, filter);
    }

    protected override void OnPause()
    {
        if (_receiver is not null)
        {
            UnregisterReceiver(_receiver);
            _receiver.Dispose();
            _receiver = null;
        }
        base.OnPause();
    }

    private sealed class BarcodeReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != "com.service.scanner.data") return;
            var text = intent.GetStringExtra("ScanCode");
            if (!string.IsNullOrEmpty(text))
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => ScanFeed.OnScan(text));
        }
    }
}
