using StockCount.Core;

namespace StockCount.Pad.Services;

public static class CountingInteractionPreferences
{
    private const string Key = "StockCount.Interaction.SuccessToastDurationMs";

    public static int SuccessToastDurationMs
    {
        get
        {
            try
            {
                return SuccessToastPolicy.Normalize(Preferences.Default.Get(
                    Key, SuccessToastPolicy.DefaultDurationMs));
            }
            catch { return SuccessToastPolicy.DefaultDurationMs; }
        }
        set => Preferences.Default.Set(Key, SuccessToastPolicy.Normalize(value));
    }
}
