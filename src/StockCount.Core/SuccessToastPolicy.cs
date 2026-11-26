namespace StockCount.Core;

/// <summary>Presentation timing only; never gates business processing.</summary>
public static class SuccessToastPolicy
{
    public const int DefaultDurationMs = 2000;
    private static readonly int[] Durations = [500, 1000, 2000, 3000, 5000];

    public static int Normalize(int milliseconds) =>
        Array.IndexOf(Durations, milliseconds) >= 0 ? milliseconds : DefaultDurationMs;

    public static int IndexOf(int milliseconds) =>
        Array.IndexOf(Durations, Normalize(milliseconds));

    public static int DurationAt(int index) =>
        index >= 0 && index < Durations.Length ? Durations[index] : DefaultDurationMs;
}

/// <summary>Used on the UI thread. Old delayed callbacks cannot hide a newer toast.</summary>
public sealed class SuccessToastLifetime
{
    private long _version;
    private bool _active;

    public long Begin()
    {
        _active = true;
        return ++_version;
    }

    public void Invalidate()
    {
        _active = false;
        ++_version;
    }

    public bool IsCurrent(long version) => _active && version == _version;
}
