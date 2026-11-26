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
    private MediaPlayer? _fallbackPlayer;
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
            using var attributes = attributesBuilder.SetUsage(AudioUsageKind.Media)
                .SetContentType(AudioContentType.Sonification).Build();
            using var builder = new SoundPool.Builder();
            _pool = builder.SetMaxStreams(1).SetAudioAttributes(attributes).Build();
            _listener = new LoadListener((sampleId, status) => Dispatch(() =>
            {
                if (!_foreground || generation != _generation) return;
                if (status == 0)
                {
                    _ready.Add(sampleId);
                    Trace($"sample-ready id={sampleId}");
                }
                else Log($"sample-load-failed id={sampleId} status={status}");
            }));
            _pool.SetOnLoadCompleteListener(_listener);
            var context = Android.App.Application.Context;
            foreach (var kind in new[] { FeedbackKind.Success, FeedbackKind.Failure, FeedbackKind.Read })
            {
                var sample = _pool.Load(context, ResourceId(kind), 1);
                _samples[kind] = sample;
                if (sample <= 0) Log($"sample-load-rejected kind={kind}");
            }
            TraceMediaVolume("resume");
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

    private static int ResourceId(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => global::StockCount.Pad.Resource.Raw.scan_success,
        FeedbackKind.Failure or FeedbackKind.WarningUnknown => global::StockCount.Pad.Resource.Raw.scan_failure,
        FeedbackKind.Read => global::StockCount.Pad.Resource.Raw.scan_read,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private void Play(FeedbackKind kind)
    {
        var now = SystemClock.ElapsedRealtime();
        if (!FeedbackAudioPolicy.MayInterrupt(kind, _streamKind, now, _streamUntil)) return;
        StopAudio();
        var pool = _pool;
        var sampleKind = FeedbackAudioPolicy.SampleKind(kind);
        var ready = _samples.TryGetValue(sampleKind, out var sample) && sample > 0 && _ready.Contains(sample);
        if (pool is not null && ready)
        {
            try
            {
                _stream = pool.Play(sample, 1f, 1f, kind == FeedbackKind.Read ? 1 : 2, 0, 1f);
                if (_stream > 0)
                {
                    _streamKind = kind;
                    _streamUntil = now + FeedbackPolicy.AudioMilliseconds(kind);
                    Trace($"soundpool-start kind={kind} stream={_stream}");
                    return;
                }
                Log($"soundpool-zero-stream kind={kind}");
            }
            catch { Log($"soundpool-play-failed kind={kind}"); }
        }
        else Log($"soundpool-not-ready kind={kind} pool={pool is not null} sample={sample}");

        if (FeedbackAudioPolicy.CanUseFallback(kind)) PlayFallback(kind);
    }

    private void PlayFallback(FeedbackKind kind)
    {
        // No delayed replay after the next business result. Native resource
        // preparation may cost time, but never wait for playback completion.
        try
        {
            TraceMediaVolume("fallback");
            var player = MediaPlayer.Create(Android.App.Application.Context, ResourceId(kind));
            if (player is null)
            {
                Log($"mediaplayer-create-failed kind={kind}");
                return;
            }
            _fallbackPlayer = player;
            player.Completion += (_, _) => Dispatch(() =>
            {
                if (ReferenceEquals(_fallbackPlayer, player)) StopAudio();
            });
            player.Error += (_, e) =>
            {
                e.Handled = true;
                Dispatch(() =>
                {
                    if (!ReferenceEquals(_fallbackPlayer, player)) return;
                    Log($"mediaplayer-error kind={kind}");
                    StopAudio();
                });
            };
            player.Looping = false;
            player.SetVolume(1f, 1f);
            player.Start();
            _streamKind = kind;
            _streamUntil = SystemClock.ElapsedRealtime() + FeedbackPolicy.AudioMilliseconds(kind);
            Trace($"mediaplayer-start kind={kind}");
        }
        catch
        {
            Log($"mediaplayer-start-failed kind={kind}");
            StopAudio();
        }
    }

    private static void TraceMediaVolume(string stage)
    {
        try
        {
            var manager = Android.App.Application.Context.GetSystemService(Context.AudioService) as AudioManager;
            if (manager is null) return;
            var volume = manager.GetStreamVolume(Android.Media.Stream.Music);
            var max = manager.GetStreamMaxVolume(Android.Media.Stream.Music);
            Trace($"media-volume stage={stage} value={volume}/{max}");
            if (volume == 0) Log("media-volume-muted");
        }
        catch { Log("media-volume-unavailable"); }
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
        var player = _fallbackPlayer;
        _fallbackPlayer = null;
        if (player is not null)
        {
            try { player.Stop(); } catch { }
            try { player.Release(); } catch { Log("fallback-release"); }
            try { player.Dispose(); } catch { Log("fallback-dispose"); }
        }
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

    private static void Trace(string stage) =>
        Android.Util.Log.Debug("StockCountFeedback", stage);

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
