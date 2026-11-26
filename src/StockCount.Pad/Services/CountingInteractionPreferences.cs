using StockCount.Core;

namespace StockCount.Pad.Services;

public static class CountingInteractionPreferences
{
    private const string Key = "StockCount.Interaction.SuccessToastDurationMs";
    private const string HistoryKey = "StockCount.Interaction.HistoryEnabled";

    public static bool HistoryEnabled
    {
        get => HistoryVisibilityPolicy.ReadEnabled(() =>
            Preferences.Default.Get(HistoryKey, HistoryVisibilityPolicy.DefaultEnabled));
        set => Preferences.Default.Set(HistoryKey, value);
    }

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
