using Android.Content;
using Android.Media;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using StockCount.Core;
using Vibrator = Android.OS.Vibrator;

namespace StockCount.Pad.Services;

/// <summary>One process-wide feedback sink. All platform state is owned by the main thread.</summary>
public sealed class AndroidScanFeedbackService : IScanFeedbackService, IDisposable
{
    private readonly FeedbackGate _gate = new();
    private readonly Dictionary<FeedbackKind, int> _samples = new();
    private readonly HashSet<int> _ready = new();
    private ScanFeedbackOptions _options = ReadOptions();
    private SoundPool? _pool;
    private LoadListener? _listener;
    private int _stream;
    private FeedbackKind _streamKind;
    private long _streamUntil;
    private int _generation;
    private bool _foreground;
    private bool _disposed;

    public ScanFeedbackOptions Options => Volatile.Read(ref _options);

    private static ScanFeedbackOptions ReadOptions()
    {
        try { return ScanFeedbackPreferences.Load(); }
        catch { return new(); }
    }

    public void UpdateOptions(ScanFeedbackOptions options)
    {
        // Persist separately from server address, environment and pending UUID.
        ScanFeedbackPreferences.Save(options);
        Volatile.Write(ref _options, options);
        Dispatch(() =>
        {
            if (!options.AllowsSound(_streamKind)) StopAudio();
            if (!options.VibrationEnabled || !options.SuccessEnabled || !options.FailureEnabled)
                CancelVibration();
        });
    }

    public void Resume() => Dispatch(() =>
    {
        if (_disposed || _foreground) return;
        _foreground = true;
        var generation = ++_generation;
        try
        {
            using var attributesBuilder = new AudioAttributes.Builder();
            using var attributes = attributesBuilder.SetUsage(AudioUsageKind.AssistanceSonification)
                .SetContentType(AudioContentType.Sonification).Build();
            using var builder = new SoundPool.Builder();
            _pool = builder.SetMaxStreams(1).SetAudioAttributes(attributes).Build();
            _listener = new LoadListener((sampleId, status) => Dispatch(() =>
            {
                if (!_foreground || generation != _generation) return;
                if (status == 0) _ready.Add(sampleId);
                else Log("sample-load");
            }));
            _pool.SetOnLoadCompleteListener(_listener);
            var context = Android.App.Application.Context;
            _samples[FeedbackKind.Success] = _pool.Load(context, global::StockCount.Pad.Resource.Raw.scan_success, 1);
            _samples[FeedbackKind.Failure] = _pool.Load(context, global::StockCount.Pad.Resource.Raw.scan_failure, 1);
            _samples[FeedbackKind.Read] = _pool.Load(context, global::StockCount.Pad.Resource.Raw.scan_read, 1);
        }
        catch { Log("initialize"); ReleaseAudio(); }
    });

    public void Suspend() => Dispatch(SuspendCore);

    private void SuspendCore()
    {
        _foreground = false;
        ++_generation;
        CancelVibration();
        ReleaseAudio();
    }

    public void Notify(FeedbackEvent feedback) => Dispatch(() =>
    {
        if (!_foreground || _disposed) return;
        var options = Options;
        if (!options.AllowsSound(feedback.Kind) && !options.AllowsVibration(feedback.Kind)) return;
        if (_gate.Accept(feedback, SystemClock.ElapsedRealtime())) Emit(feedback.Kind);
    });

    public void Preview(FeedbackKind kind) => Dispatch(() =>
    {
        if (_foreground && !_disposed) Emit(kind);
    });

    private void Emit(FeedbackKind kind)
    {
        var options = Options;
        if (options.AllowsSound(kind))
        {
            try { Play(kind); }
            catch { Log("play"); }
        }
        if (options.AllowsVibration(kind))
        {
            try
            {
                var ms = FeedbackPolicy.VibrationMilliseconds(kind);
                var vibrator = GetVibrator();
                if (ms > 0 && vibrator?.HasVibrator == true)
                {
                    using var effect = VibrationEffect.CreateOneShot(ms, VibrationEffect.DefaultAmplitude);
                    vibrator.Vibrate(effect);
                }
            }
            catch { Log("vibrate"); }
        }
    }

    private void Play(FeedbackKind kind)
    {
        var pool = _pool;
        if (pool is null) return;
        var sampleKind = kind == FeedbackKind.WarningUnknown ? FeedbackKind.Failure : kind;
        if (!_samples.TryGetValue(sampleKind, out var sample) || sample <= 0 || !_ready.Contains(sample))
            return; // Early load/failure: vibration still works; never replay a stale business result.
        var now = SystemClock.ElapsedRealtime();
        if (kind == FeedbackKind.Read && _streamKind != FeedbackKind.Read && now < _streamUntil)
            return; // Never interrupt business feedback with an optional hardware read tone.
        StopAudio();
        _stream = pool.Play(sample, 1f, 1f, kind == FeedbackKind.Read ? 1 : 2, 0, 1f);
        _streamKind = kind;
        _streamUntil = _stream != 0 ? now + FeedbackPolicy.AudioMilliseconds(kind) : 0;
    }

    private static Vibrator? GetVibrator()
    {
        var context = Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var manager = context.GetSystemService(Context.VibratorManagerService) as VibratorManager;
            return manager?.DefaultVibrator;
        }
        return context.GetSystemService(Context.VibratorService) as Vibrator;
    }

    private static void CancelVibration()
    {
        try { var vibrator = GetVibrator(); vibrator?.Cancel(); }
        catch { Log("cancel-vibration"); }
    }

    private void StopAudio()
    {
        try { if (_stream != 0) _pool?.Stop(_stream); }
        catch { Log("stop"); }
        _stream = 0;
        _streamUntil = 0;
        _streamKind = FeedbackKind.None;
    }

    private void ReleaseAudio()
    {
        StopAudio();
        _ready.Clear();
        _samples.Clear();
        var pool = _pool;
        _pool = null;
        try { pool?.SetOnLoadCompleteListener(null); }
        catch { Log("detach-listener"); }
        try { pool?.Release(); }
        catch { Log("release"); }
        try { pool?.Dispose(); _listener?.Dispose(); }
        catch { Log("dispose"); }
        _listener = null;
    }

    private static void Dispatch(Action action)
    {
        void Safe()
        {
            try { action(); }
            catch { Log("feedback"); }
        }
        try
        {
            if (MainThread.IsMainThread) Safe();
            else MainThread.BeginInvokeOnMainThread(Safe);
        }
        catch { Log("dispatch"); }
    }

    private static void Log(string stage) =>
        Android.Util.Log.Warn("StockCountFeedback", stage); // No payload, barcode, UUID or secret.

    public void Dispose() => Dispatch(() =>
    {
        if (_disposed) return;
        _disposed = true;
        SuspendCore();
    });

    private sealed class LoadListener(Action<int, int> complete) :
        Java.Lang.Object, SoundPool.IOnLoadCompleteListener
    {
        public void OnLoadComplete(SoundPool? soundPool, int sampleId, int status) =>
            complete(sampleId, status);
    }
}
