using System.Diagnostics;
using System.Globalization;
using RedisDesktop.Core;
using StackExchange.Redis;

namespace RedisDesktop.Infrastructure;

public sealed class RedisBenchmarkService : IBenchmarkService
{
    private readonly RedisSessionFactory _factory;
    private readonly ICommandLog _commandLog;

    public RedisBenchmarkService(RedisSessionFactory factory, ICommandLog commandLog)
    {
        _factory = factory;
        _commandLog = commandLog;
    }

    public async Task<BenchmarkReport> RunAsync(
        BenchmarkSpec spec,
        IProgress<BenchmarkTick>? progress,
        CancellationToken cancellationToken = default)
    {
        spec = BenchmarkLimits.Normalize(spec);
        if (spec.Config.ReadOnly)
        {
            throw new ReadOnlyException("BENCHMARK");
        }

        var started = Stopwatch.StartNew();
        Log(spec, "BENCHMARK", "started " + Describe(spec), 0, true, null);

        long completed = 0;
        long failed = 0;
        long bytes = 0;
        var nextIndex = 0;
        var latency = new LatencyAccumulator();
        var qpsHistory = new List<double>(BenchmarkLimits.MaxChartPoints);
        using var reporterCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? error = null;
        var cancelled = false;

        try
        {
            await using var pool = await _factory
                .OpenWorkerPoolAsync(spec.Config, spec.Secrets, spec.Concurrency, cancellationToken)
                .ConfigureAwait(false);

            var payload = new byte[spec.ValueBytes];
            Array.Fill(payload, (byte)'x');
            var value = (RedisValue)payload;
            var deadline = spec.UseDuration
                ? TimeSpan.FromSeconds(spec.DurationSeconds!.Value)
                : (TimeSpan?)null;
            var target = spec.UseDuration ? long.MaxValue : spec.Requests;

            var reporter = ReportLoopAsync(
                () => (Volatile.Read(ref completed), Volatile.Read(ref failed), Volatile.Read(ref bytes)),
                latency,
                qpsHistory,
                started,
                progress,
                reporterCts.Token);

            try
            {
                var workers = pool.Databases
                    .Select(database => Task.Run(
                        () => RunWorkerAsync(
                            database,
                            spec,
                            value,
                            target,
                            deadline,
                            started,
                            () => Interlocked.Add(ref nextIndex, spec.Pipeline) - spec.Pipeline,
                            (n, nfail, nbytes, elapsed) =>
                            {
                                latency.Add(elapsed);
                                Interlocked.Add(ref completed, n);
                                if (nfail > 0)
                                {
                                    Interlocked.Add(ref failed, nfail);
                                }

                                if (nbytes > 0)
                                {
                                    Interlocked.Add(ref bytes, nbytes);
                                }
                            },
                            cancellationToken),
                        cancellationToken))
                    .ToArray();

                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                await reporterCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await reporter.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected when the run finishes
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        started.Stop();
        var snapshot = latency.Snapshot();
        var elapsedSeconds = Math.Max(started.Elapsed.TotalSeconds, 0.0001);
        var report = new BenchmarkReport(
            Volatile.Read(ref completed),
            Volatile.Read(ref failed),
            Volatile.Read(ref bytes),
            Volatile.Read(ref completed) / elapsedSeconds,
            started.Elapsed.TotalSeconds,
            snapshot.P50,
            snapshot.P95,
            snapshot.P99,
            snapshot.Max,
            qpsHistory.ToArray(),
            snapshot.Histogram,
            cancelled,
            error?.Message);

        progress?.Report(ToTick(report, report.OverallQps, finished: true));
        Log(
            spec,
            "BENCHMARK",
            "finished " + DescribeResult(report),
            report.ElapsedSeconds * 1000,
            error is null,
            error?.Message);
        if (error is not null)
        {
            throw error;
        }

        return report;
    }

    public async Task<long> DeleteTestKeysAsync(BenchmarkSpec spec, CancellationToken cancellationToken = default)
    {
        spec = BenchmarkLimits.Normalize(spec);
        if (spec.Config.ReadOnly)
        {
            throw new ReadOnlyException("UNLINK");
        }

        await using var pool = await _factory
            .OpenWorkerPoolAsync(spec.Config, spec.Secrets, 1, cancellationToken)
            .ConfigureAwait(false);
        var match = spec.KeyPrefix + "*";
        var db = pool.Databases[0];
        long deleted = 0;
        foreach (var server in GetMasters(pool.Primary))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long cursor = 0;
            do
            {
                var result = await server.ExecuteAsync("SCAN", cursor, "MATCH", match, "COUNT", 500)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                var (next, keys) = ParseScan(result);
                cursor = next;
                if (keys.Count > 0)
                {
                    var redisKeys = keys.Select(key => (RedisKey)key.Value).ToArray();
                    deleted += await db.KeyDeleteAsync(redisKeys).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            } while (cursor != 0 && !cancellationToken.IsCancellationRequested);
        }

        Log(spec, "UNLINK", spec.KeyPrefix + "* x" + deleted.ToString(CultureInfo.InvariantCulture), 0, true, null);
        return deleted;
    }

    private static async Task RunWorkerAsync(
        IDatabase db,
        BenchmarkSpec spec,
        RedisValue payload,
        long target,
        TimeSpan? deadline,
        Stopwatch started,
        Func<long> takeIndex,
        Action<int, int, long, TimeSpan> onBatch,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (deadline is not null && started.Elapsed >= deadline.Value)
            {
                return;
            }

            var startIndex = takeIndex();
            if (startIndex >= target)
            {
                return;
            }

            var count = (int)Math.Min(spec.Pipeline, target - startIndex);
            if (count <= 0)
            {
                return;
            }

            var watch = Stopwatch.StartNew();
            try
            {
                var (_, nbytes) = await ExecuteBatchAsync(db, spec, payload, startIndex, count).ConfigureAwait(false);
                watch.Stop();
                onBatch(count, 0, nbytes, watch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                watch.Stop();
                onBatch(count, count, 0, watch.Elapsed);
            }
        }
    }

    internal static async Task<(int Ops, long Bytes)> ExecuteBatchAsync(
        IDatabase db,
        BenchmarkSpec spec,
        RedisValue payload,
        long startIndex,
        int count)
    {
        if (count <= 1)
        {
            var kind = spec.Commands[(int)(startIndex % spec.Commands.Count)];
            var key = Key(spec.KeyPrefix, startIndex);
            await ExecuteAsync(db, kind, key, payload).ConfigureAwait(false);
            return (CommandOps(kind), EstimateBytes(kind, key, payload));
        }

        var batch = db.CreateBatch();
        var tasks = new List<Task>(count * 2);
        var ops = 0;
        long bytes = 0;
        for (var i = 0; i < count; i++)
        {
            var kind = spec.Commands[(int)((startIndex + i) % spec.Commands.Count)];
            var key = Key(spec.KeyPrefix, startIndex + i);
            Queue(batch, kind, key, payload, tasks);
            ops += CommandOps(kind);
            bytes += EstimateBytes(kind, key, payload);
        }

        batch.Execute();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return (ops, bytes);
    }

    private static Task ExecuteAsync(IDatabaseAsync db, BenchmarkCommandKind kind, RedisKey key, RedisValue payload)
        => kind switch
        {
            BenchmarkCommandKind.Ping => AsTask(db.PingAsync()),
            BenchmarkCommandKind.Set => AsTask(db.StringSetAsync(key, payload)),
            BenchmarkCommandKind.Get => AsTask(db.StringGetAsync(key)),
            BenchmarkCommandKind.Incr => AsTask(db.StringIncrementAsync(key)),
            BenchmarkCommandKind.Hash => AsTask(db.HashSetAsync(key, "f", payload)),
            BenchmarkCommandKind.List => ExecuteListAsync(db, key, payload),
            _ => AsTask(db.PingAsync())
        };

    private static void Queue(
        IDatabaseAsync db,
        BenchmarkCommandKind kind,
        RedisKey key,
        RedisValue payload,
        List<Task> tasks)
    {
        switch (kind)
        {
            case BenchmarkCommandKind.Ping:
                tasks.Add(AsTask(db.PingAsync()));
                break;
            case BenchmarkCommandKind.Set:
                tasks.Add(AsTask(db.StringSetAsync(key, payload)));
                break;
            case BenchmarkCommandKind.Get:
                tasks.Add(AsTask(db.StringGetAsync(key)));
                break;
            case BenchmarkCommandKind.Incr:
                tasks.Add(AsTask(db.StringIncrementAsync(key)));
                break;
            case BenchmarkCommandKind.Hash:
                tasks.Add(AsTask(db.HashSetAsync(key, "f", payload)));
                break;
            case BenchmarkCommandKind.List:
                tasks.Add(AsTask(db.ListLeftPushAsync(key, payload)));
                tasks.Add(AsTask(db.ListLeftPopAsync(key)));
                break;
            default:
                tasks.Add(AsTask(db.PingAsync()));
                break;
        }
    }

    private static async Task ExecuteListAsync(IDatabaseAsync db, RedisKey key, RedisValue payload)
    {
        await db.ListLeftPushAsync(key, payload).ConfigureAwait(false);
        await db.ListLeftPopAsync(key).ConfigureAwait(false);
    }

    private static Task AsTask(Task task) => task;

    private static Task AsTask<T>(Task<T> task) => task;

    private static RedisKey Key(string prefix, long index)
        => prefix + index.ToString(CultureInfo.InvariantCulture);

    private static int CommandOps(BenchmarkCommandKind kind)
        => kind == BenchmarkCommandKind.List ? 2 : 1;

    private static long EstimateBytes(BenchmarkCommandKind kind, RedisKey key, RedisValue payload)
    {
        if (kind is BenchmarkCommandKind.Ping or BenchmarkCommandKind.Get or BenchmarkCommandKind.Incr)
        {
            return key.ToString().Length;
        }

        return key.ToString().Length + (payload.HasValue ? payload.Length() : 0);
    }

    private static async Task ReportLoopAsync(
        Func<(long Completed, long Failed, long Bytes)> snapshot,
        LatencyAccumulator latency,
        List<double> qpsHistory,
        Stopwatch started,
        IProgress<BenchmarkTick>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(BenchmarkLimits.SampleIntervalMs));
        long previousCompleted = 0;
        var previousElapsed = 0d;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var (completed, failed, bytes) = snapshot();
                var elapsed = Math.Max(started.Elapsed.TotalSeconds, 0.0001);
                var instant = (completed - previousCompleted) / Math.Max(elapsed - previousElapsed, 0.0001);
                previousCompleted = completed;
                previousElapsed = elapsed;
                qpsHistory.Add(instant);
                if (qpsHistory.Count > BenchmarkLimits.MaxChartPoints)
                {
                    qpsHistory.RemoveAt(0);
                }

                var lat = latency.Snapshot();
                progress.Report(new BenchmarkTick(
                    completed,
                    failed,
                    bytes,
                    instant,
                    completed / elapsed,
                    started.Elapsed.TotalSeconds,
                    lat.P50,
                    lat.P95,
                    lat.P99,
                    lat.Max,
                    qpsHistory.ToArray(),
                    lat.Histogram,
                    false,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            // run finished
        }
    }

    private static BenchmarkTick ToTick(BenchmarkReport report, double instantQps, bool finished)
        => new(
            report.Completed,
            report.Failed,
            report.Bytes,
            instantQps,
            report.OverallQps,
            report.ElapsedSeconds,
            report.P50,
            report.P95,
            report.P99,
            report.Max,
            report.QpsHistory,
            report.Histogram,
            finished,
            report.Error);

    private void Log(BenchmarkSpec spec, string command, string details, double elapsedMs, bool success, string? error)
    {
        _commandLog.Append(new CommandLogEntry(
            DateTimeOffset.Now,
            spec.Config.Name,
            command,
            details,
            elapsedMs,
            success,
            error));
    }

    private static string Describe(BenchmarkSpec spec)
    {
        var mode = spec.UseDuration
            ? spec.DurationSeconds + "s"
            : spec.Requests.ToString(CultureInfo.InvariantCulture);
        return $"{spec.CommandSummary} {mode} conc={spec.Concurrency} pipeline={spec.Pipeline} value={spec.ValueBytes}B";
    }

    private static string DescribeResult(BenchmarkReport report)
        => $"{report.Succeeded}/{report.Completed} ok qps={report.OverallQps:0.#} p99={report.P99:0.###}ms";

    internal static IServer[] GetMasters(ConnectionMultiplexer multiplexer)
    {
        var servers = multiplexer.GetEndPoints()
            .Select(endpoint =>
            {
                try
                {
                    return multiplexer.GetServer(endpoint);
                }
                catch
                {
                    return null;
                }
            })
            .Where(server => server is { IsConnected: true, IsReplica: false })
            .Cast<IServer>()
            .ToArray();

        return servers.Length > 0
            ? servers
            : multiplexer.GetEndPoints().Select(endpoint => multiplexer.GetServer(endpoint)).ToArray();
    }

    internal static (long Cursor, IReadOnlyList<RedisKeyBytes> Keys) ParseScan(RedisResult result)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            return (0, []);
        }

        var rows = (RedisResult[])result!;
        var cursor = rows.Length > 0 ? long.Parse(rows[0].ToString()!) : 0;
        var keys = new List<RedisKeyBytes>();
        if (rows.Length > 1 && rows[1].Resp2Type == ResultType.Array)
        {
            foreach (var item in (RedisResult[])rows[1]!)
            {
                if (item.IsNull)
                {
                    continue;
                }

                keys.Add(new RedisKeyBytes((byte[])item!));
            }
        }

        return (cursor, keys);
    }
}
