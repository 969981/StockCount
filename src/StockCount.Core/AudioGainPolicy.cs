namespace StockCount.Core;

public static class AudioGainPolicy
{
    public const int MinStep = 2;
    public const int DefaultStep = 4;
    public const int MaxStep = 12;

    public static int Normalize(int step) => step is >= MinStep and <= MaxStep ? step : DefaultStep;
    public static int FromSlider(double value) => double.IsFinite(value)
        ? (int)Math.Round(Math.Clamp(value, MinStep, MaxStep), MidpointRounding.AwayFromZero)
        : DefaultStep;
    public static double Multiplier(int step) => Normalize(step) / 4.0;
    public static int AssetStep(FeedbackKind kind, int step) =>
        kind == FeedbackKind.Read ? DefaultStep : Math.Max(DefaultStep, Normalize(step));
    public static float PlaybackVolume(FeedbackKind kind, int step) =>
        kind == FeedbackKind.Read ? 1f : (float)Math.Min(1.0, Multiplier(step));
}
