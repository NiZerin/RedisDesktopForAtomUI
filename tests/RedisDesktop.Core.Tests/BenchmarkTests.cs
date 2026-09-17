using RedisDesktop.Core;

namespace RedisDesktop.Core.Tests;

public class BenchmarkTests
{
    [Fact]
    public void KeyPrefix_uses_connection_id_without_braces()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var prefix = BenchmarkLimits.KeyPrefix(id);
        Assert.Equal("__rdbench:aaaaaaaabbbbccccddddeeeeeeeeeeee:", prefix);
        Assert.DoesNotContain("{", prefix);
    }

    [Fact]
    public void Normalize_defaults_empty_commands_to_ping_and_clamps()
    {
        var spec = BenchmarkLimits.Normalize(new BenchmarkSpec(
            new ConnectionConfig { Id = Guid.NewGuid(), Name = "demo" },
            new ConnectionSecrets(),
            [],
            Requests: 0,
            Concurrency: 0,
            Pipeline: 0,
            ValueBytes: 0,
            KeyPrefix: " ",
            DurationSeconds: 99999));

        Assert.Equal([BenchmarkCommandKind.Ping], spec.Commands);
        Assert.Equal(1, spec.Requests);
        Assert.Equal(1, spec.Concurrency);
        Assert.Equal(1, spec.Pipeline);
        Assert.Equal(1, spec.ValueBytes);
        Assert.Equal(BenchmarkLimits.MaxDurationSeconds, spec.DurationSeconds);
        Assert.StartsWith("__rdbench:", spec.KeyPrefix);
        Assert.EndsWith(":", spec.KeyPrefix);
    }

    [Fact]
    public void Command_write_flags_match_redis_benchmark_mix()
    {
        Assert.False(BenchmarkCommands.IsWrite(BenchmarkCommandKind.Ping));
        Assert.False(BenchmarkCommands.IsWrite(BenchmarkCommandKind.Get));
        Assert.True(BenchmarkCommands.IsWrite(BenchmarkCommandKind.Set));
        Assert.True(BenchmarkCommands.WritesKeys(BenchmarkCommandKind.Set));
        Assert.False(BenchmarkCommands.WritesKeys(BenchmarkCommandKind.Get));
        Assert.Equal("LPUSH+LPOP", BenchmarkCommands.Label(BenchmarkCommandKind.List));
    }

    [Fact]
    public void Percentile_interpolates_sorted_samples()
    {
        int[] samples = [1000, 2000, 3000, 4000, 5000];
        Assert.Equal(3.0, LatencyAccumulator.Percentile(samples, 50), 3);
        Assert.Equal(4.8, LatencyAccumulator.Percentile(samples, 95), 3);
        Assert.Equal(0, LatencyAccumulator.Percentile([], 99));
    }

    [Fact]
    public void Histogram_puts_samples_into_ms_buckets()
    {
        int[] samples = [200, 800, 1500, 120000];
        var buckets = LatencyAccumulator.Histogram(samples);
        Assert.Equal("<0.5", buckets[0].Label);
        Assert.Equal(1, buckets[0].Count);
        Assert.Equal("100+", buckets[^1].Label);
        Assert.Equal(1, buckets[^1].Count);
        Assert.Equal(samples.Length, buckets.Sum(item => item.Count));
    }

    [Fact]
    public void Accumulator_tracks_max_and_p99()
    {
        var acc = new LatencyAccumulator();
        for (var i = 1; i <= 100; i++)
        {
            acc.Add(TimeSpan.FromMilliseconds(i));
        }

        var snap = acc.Snapshot();
        Assert.Equal(100, snap.Samples);
        Assert.InRange(snap.P50, 49, 51);
        Assert.InRange(snap.P99, 98, 100);
        Assert.Equal(100, snap.Max, 3);
    }
}
