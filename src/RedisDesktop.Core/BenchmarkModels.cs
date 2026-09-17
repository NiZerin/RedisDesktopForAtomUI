using System.Globalization;

namespace RedisDesktop.Core;

public enum BenchmarkCommandKind
{
    Ping,
    Set,
    Get,
    Incr,
    List,
    Hash
}

public sealed record BenchmarkSpec(
    ConnectionConfig Config,
    ConnectionSecrets Secrets,
    IReadOnlyList<BenchmarkCommandKind> Commands,
    int Requests,
    int Concurrency,
    int Pipeline,
    int ValueBytes,
    string KeyPrefix,
    int? DurationSeconds)
{
    public bool UseDuration => DurationSeconds is > 0;

    public bool WritesKeys => Commands.Any(BenchmarkCommands.WritesKeys);

    public string CommandSummary => string.Join("+", Commands.Select(BenchmarkCommands.Label));
}

public sealed record BenchmarkHistogramBucket(string Label, int Count);

public sealed record BenchmarkTick(
    long Completed,
    long Failed,
    long Bytes,
    double InstantQps,
    double OverallQps,
    double ElapsedSeconds,
    double P50,
    double P95,
    double P99,
    double Max,
    IReadOnlyList<double> QpsHistory,
    IReadOnlyList<BenchmarkHistogramBucket> Histogram,
    bool Finished,
    string? Error);

public sealed record BenchmarkReport(
    long Completed,
    long Failed,
    long Bytes,
    double OverallQps,
    double ElapsedSeconds,
    double P50,
    double P95,
    double P99,
    double Max,
    IReadOnlyList<double> QpsHistory,
    IReadOnlyList<BenchmarkHistogramBucket> Histogram,
    bool Cancelled,
    string? Error)
{
    public long Succeeded => Math.Max(0, Completed - Failed);

    public string ThroughputText
    {
        get
        {
            if (ElapsedSeconds <= 0 || Bytes <= 0)
            {
                return "0 B/s";
            }

            return ValueViewerPipeline.FormatByteSize((long)(Bytes / ElapsedSeconds)) + "/s";
        }
    }
}

public static class BenchmarkCommands
{
    public static string Label(BenchmarkCommandKind kind)
        => kind switch
        {
            BenchmarkCommandKind.Ping => "PING",
            BenchmarkCommandKind.Set => "SET",
            BenchmarkCommandKind.Get => "GET",
            BenchmarkCommandKind.Incr => "INCR",
            BenchmarkCommandKind.List => "LPUSH+LPOP",
            BenchmarkCommandKind.Hash => "HSET",
            _ => kind.ToString().ToUpperInvariant()
        };

    public static bool WritesKeys(BenchmarkCommandKind kind)
        => kind is BenchmarkCommandKind.Set
            or BenchmarkCommandKind.Incr
            or BenchmarkCommandKind.List
            or BenchmarkCommandKind.Hash;

    public static bool IsWrite(BenchmarkCommandKind kind)
        => kind is not BenchmarkCommandKind.Ping and not BenchmarkCommandKind.Get;
}

public static class BenchmarkLimits
{
    public const int DefaultRequests = 1_000;

    public const int SuggestedRequests = 10_000;

    public const int MaxRequests = 1_000_000;

    public const int WarnRequests = 100_000;

    public const int DefaultConcurrency = 10;

    public const int MaxConcurrency = 64;

    public const int DefaultPipeline = 1;

    public const int MaxPipeline = 1_000;

    public const int DefaultValueBytes = 64;

    public const int MaxValueBytes = 64 * 1024;

    public const int DefaultDurationSeconds = 10;

    public const int MaxDurationSeconds = 3_600;

    public const int WarnDurationSeconds = 60;

    public const int SampleIntervalMs = 200;

    public const int MaxChartPoints = 300;

    public const int MaxLatencySamples = 200_000;

    public static string KeyPrefix(Guid connectionId)
        => "__rdbench:" + connectionId.ToString("N") + ":";

    public static BenchmarkSpec Normalize(BenchmarkSpec spec)
    {
        var commands = spec.Commands.Count == 0
            ? [BenchmarkCommandKind.Ping]
            : spec.Commands.Distinct().ToArray();
        var prefix = string.IsNullOrWhiteSpace(spec.KeyPrefix)
            ? KeyPrefix(spec.Config.Id)
            : spec.KeyPrefix.Trim();
        if (!prefix.EndsWith(":", StringComparison.Ordinal))
        {
            prefix += ":";
        }

        return spec with
        {
            Commands = commands,
            Requests = Math.Clamp(spec.Requests, 1, MaxRequests),
            Concurrency = Math.Clamp(spec.Concurrency, 1, MaxConcurrency),
            Pipeline = Math.Clamp(spec.Pipeline, 1, MaxPipeline),
            ValueBytes = Math.Clamp(spec.ValueBytes, 1, MaxValueBytes),
            KeyPrefix = prefix,
            DurationSeconds = spec.DurationSeconds is null
                ? null
                : Math.Clamp(spec.DurationSeconds.Value, 1, MaxDurationSeconds)
        };
    }
}

public sealed class LatencyAccumulator
{
    private static readonly double[] BucketEdgesMs = [0.5, 1, 2, 5, 10, 20, 50, 100];

    private readonly object _gate = new();
    private readonly List<int> _micros = [];
    private int _accepted;
    private long _maxMicros;

    public void Add(TimeSpan elapsed)
    {
        var micros = elapsed.Ticks <= 0
            ? 0
            : (int)Math.Clamp(elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1000d), 0, int.MaxValue);
        lock (_gate)
        {
            _accepted++;
            if (micros > _maxMicros)
            {
                _maxMicros = micros;
            }

            if (_micros.Count < BenchmarkLimits.MaxLatencySamples)
            {
                _micros.Add(micros);
                return;
            }

            var slot = Random.Shared.Next(BenchmarkLimits.MaxLatencySamples);
            _micros[slot] = micros;
        }
    }

    public LatencySnapshot Snapshot()
    {
        int[] copy;
        int accepted;
        long maxMicros;
        lock (_gate)
        {
            copy = _micros.ToArray();
            accepted = _accepted;
            maxMicros = _maxMicros;
        }

        Array.Sort(copy);
        return new LatencySnapshot(
            accepted,
            Percentile(copy, 50),
            Percentile(copy, 95),
            Percentile(copy, 99),
            maxMicros / 1000d,
            Histogram(copy));
    }

    public static double Percentile(IReadOnlyList<int> sortedMicros, double percentile)
    {
        if (sortedMicros.Count == 0)
        {
            return 0;
        }

        var rank = (percentile / 100d) * (sortedMicros.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return sortedMicros[low] / 1000d;
        }

        var weight = rank - low;
        return ((sortedMicros[low] * (1 - weight)) + (sortedMicros[high] * weight)) / 1000d;
    }

    public static IReadOnlyList<BenchmarkHistogramBucket> Histogram(IReadOnlyList<int> sortedMicros)
    {
        var counts = new int[BucketEdgesMs.Length + 1];
        foreach (var micros in sortedMicros)
        {
            var ms = micros / 1000d;
            var bucket = BucketEdgesMs.Length;
            for (var i = 0; i < BucketEdgesMs.Length; i++)
            {
                if (ms < BucketEdgesMs[i])
                {
                    bucket = i;
                    break;
                }
            }

            counts[bucket]++;
        }

        var buckets = new BenchmarkHistogramBucket[counts.Length];
        for (var i = 0; i < counts.Length; i++)
        {
            buckets[i] = new BenchmarkHistogramBucket(BucketLabel(i), counts[i]);
        }

        return buckets;
    }

    private static string BucketLabel(int index)
    {
        if (index == 0)
        {
            return "<0.5";
        }

        if (index >= BucketEdgesMs.Length)
        {
            return "100+";
        }

        var prev = BucketEdgesMs[index - 1].ToString("0.#", CultureInfo.InvariantCulture);
        var next = BucketEdgesMs[index].ToString("0.#", CultureInfo.InvariantCulture);
        return prev + "-" + next;
    }
}

public readonly record struct LatencySnapshot(
    int Samples,
    double P50,
    double P95,
    double P99,
    double Max,
    IReadOnlyList<BenchmarkHistogramBucket> Histogram);

public interface IBenchmarkService
{
    Task<BenchmarkReport> RunAsync(
        BenchmarkSpec spec,
        IProgress<BenchmarkTick>? progress,
        CancellationToken cancellationToken = default);

    Task<long> DeleteTestKeysAsync(BenchmarkSpec spec, CancellationToken cancellationToken = default);
}
