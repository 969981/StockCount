using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StockCount.Core;

// Fixed stage names only: never collect QR, quantities, credentials or URLs.
public enum ClientPerfStage
{
    ScanQueue, ScanTotal, ScanWorkflow, UiRefresh, QuantityFocus,
    SubmitTotal, SubmitWorkflow, ConfirmCleanup, CheckTotal,
    RequestHeaders, HttpResponse, JsonDecode, DraftRead, DraftWrite, DraftClear
}

public sealed class ClientPerformance
{
    public static ClientPerformance Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Queue<(ClientPerfStage Stage, double Ms)> _samples = new();
    private readonly int _capacity;

    public ClientPerformance(int capacity = 512)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public Measurement Measure(ClientPerfStage stage) => new(this, stage, Stopwatch.GetTimestamp());
    public void RecordSince(ClientPerfStage stage, long start) =>
        Record(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

    public void Record(ClientPerfStage stage, double ms)
    {
        if (!double.IsFinite(ms) || ms < 0) return;
        lock (_sync)
        {
            if (_samples.Count == _capacity) _samples.Dequeue();
            _samples.Enqueue((stage, ms));
        }
    }

    public void Clear() { lock (_sync) _samples.Clear(); }
    public sealed record Summary(ClientPerfStage Stage, int Count, double LatestMs, double P50Ms, double P95Ms, double MaxMs);
    public Summary[] Snapshot()
    {
        (ClientPerfStage Stage, double Ms)[] snapshot;
        lock (_sync) snapshot = _samples.ToArray();
        return snapshot.GroupBy(x => x.Stage).OrderBy(g => g.Key).Select(g =>
        {
            var ordered = g.Select(x => x.Ms).OrderBy(x => x).ToArray();
            double Percentile(double p) => ordered[Math.Max(0, (int)Math.Ceiling(p * ordered.Length) - 1)];
            return new Summary(g.Key, ordered.Length, g.Last().Ms, Percentile(.5), Percentile(.95), ordered[^1]);
        }).ToArray();
    }

    public string FormatReport()
    {
        var summary = Snapshot();
        var b = new StringBuilder("PDA 性能诊断（内存采样，ms；阶段可能嵌套，含失败/等待）\n");
        if (summary.Length == 0) return b.Append("暂无采样，请先扫码或提交。").ToString();
        foreach (var s in summary)
            b.AppendLine(FormattableString.Invariant($"{s.Stage}: n={s.Count}, last={s.LatestMs:F1}, P50={s.P50Ms:F1}, P95={s.P95Ms:F1}, max={s.MaxMs:F1}"));
        return b.ToString();
    }

    public readonly struct Measurement(ClientPerformance owner, ClientPerfStage stage, long start) : IDisposable
    {
        public void Dispose() => owner.RecordSince(stage, start);
    }
}
