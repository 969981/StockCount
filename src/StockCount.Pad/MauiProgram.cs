using Microsoft.Extensions.Logging;
using StockCount.Pad.Services;

namespace StockCount.Pad;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<StockCountApiClient>();
        builder.Services.AddSingleton<StockCount.Core.IPendingSubmissionStore,
            SecurePendingSubmissionStore>();
        builder.Services.AddTransient<CountListPage>();
        builder.Services.AddTransient<SettingsPage>();
        return builder.Build();
    }
}
