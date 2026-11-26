using StockCount.Core;

namespace StockCount.Pad.Services;

internal static class ScanFeedbackPreferences
{
    private const string Prefix = "StockCount.Feedback.";

    public static ScanFeedbackOptions Load() => new(
        Preferences.Default.Get(Prefix + "SoundEnabled", true),
        Preferences.Default.Get(Prefix + "VibrationEnabled", true),
        Preferences.Default.Get(Prefix + "SuccessEnabled", true),
        Preferences.Default.Get(Prefix + "FailureEnabled", true),
        Preferences.Default.Get(Prefix + "ReadEnabled", false));

    public static void Save(ScanFeedbackOptions options)
    {
        Preferences.Default.Set(Prefix + "SoundEnabled", options.SoundEnabled);
        Preferences.Default.Set(Prefix + "VibrationEnabled", options.VibrationEnabled);
        Preferences.Default.Set(Prefix + "SuccessEnabled", options.SuccessEnabled);
        Preferences.Default.Set(Prefix + "FailureEnabled", options.FailureEnabled);
        Preferences.Default.Set(Prefix + "ReadEnabled", options.ReadEnabled);
    }
}
