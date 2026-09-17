using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class BenchmarkTabViewModel : ViewModelBase, IWorkspaceTab
{
    private readonly MainWindowViewModel _owner;
    private readonly IBenchmarkService _benchmark;
    private CancellationTokenSource? _cts;

    public BenchmarkTabViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection)
    {
        _owner = owner;
        _benchmark = owner.Benchmark;
        Connection = connection;
        Title = $"{connection.Config.Name} {owner.Loc.Benchmark}";
        KeyPrefix = BenchmarkLimits.KeyPrefix(connection.Config.Id);
        StatusText = owner.Loc.BenchmarkHint;
        SummaryText = owner.Loc.BenchmarkIdle;
        ClusterHint = owner.Loc.BenchmarkClusterHint;
    }

    public UiStrings Loc => _owner.Loc;

    public string Title { get; }

    public ConnectionItemViewModel Connection { get; }

    public string KeyPrefix { get; }

    public override string ToString() => Title;

    public bool CanWrite => !Connection.Config.ReadOnly;

    public bool ShowClusterHint => Connection.Config.Kind == RedisDeploymentKind.Cluster;

    public ObservableCollection<BenchmarkHistogramBarViewModel> LatencyBars { get; } = [];

    [ObservableProperty]
    private bool _includePing = true;

    [ObservableProperty]
    private bool _includeSet;

    [ObservableProperty]
    private bool _includeGet;

    [ObservableProperty]
    private bool _includeIncr;

    [ObservableProperty]
    private bool _includeList;

    [ObservableProperty]
    private bool _includeHash;

    [ObservableProperty]
    private decimal _requests = BenchmarkLimits.DefaultRequests;

    [ObservableProperty]
    private decimal _concurrency = BenchmarkLimits.DefaultConcurrency;

    [ObservableProperty]
    private decimal _pipeline = BenchmarkLimits.DefaultPipeline;

    [ObservableProperty]
    private decimal _valueBytes = BenchmarkLimits.DefaultValueBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRequests))]
    private bool _useDuration;

    [ObservableProperty]
    private decimal _durationSeconds = BenchmarkLimits.DefaultDurationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanEditRequests))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanCleanup))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _clusterHint = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _qpsPoints = [];

    [ObservableProperty]
    private string _qpsText = "0";

    [ObservableProperty]
    private string _p50Text = "0";

    [ObservableProperty]
    private string _p95Text = "0";

    [ObservableProperty]
    private string _p99Text = "0";

    [ObservableProperty]
    private string _maxText = "0";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanup))]
    private bool _hasRun;

    public bool CanEdit => CanWrite && !IsRunning && !IsCleaning;

    public bool CanEditRequests => CanEdit && !UseDuration;

    public bool CanStart => CanWrite && !IsRunning && !IsCleaning;

    public bool CanStop => IsRunning;

    public bool CanCleanup => CanWrite && !IsRunning && !IsCleaning && HasRun;

    [RelayCommand]
    public async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }

        var spec = BuildSpec();
        if (!await ConfirmStartAsync(spec))
        {
            return;
        }

        Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        HasRun = true;
        QpsPoints = [];
        LatencyBars.Clear();
        StatusText = Loc.BenchmarkRunning;
        SummaryText = Loc.Format("benchmark_opening", "正在建立压测连接…", "Opening benchmark connections…");
        try
        {
            var progress = new Progress<BenchmarkTick>(tick =>
            {
                Dispatcher.UIThread.Post(() => ApplyTick(tick));
            });
            var report = await _benchmark.RunAsync(spec, progress, _cts.Token);
            ApplyReport(report);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.BenchmarkCancelled;
            SummaryText = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format("benchmark_fail", "压测失败：{0}", "Benchmark failed: {0}", ex.Message);
            SummaryText = StatusText;
            await _owner.Prompt.ErrorAsync(Loc.Benchmark, ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Stop() => Cancel();

    [RelayCommand]
    public async Task CleanupAsync()
    {
        if (!CanCleanup)
        {
            return;
        }

        if (!await _owner.Prompt.ConfirmAsync(Loc.BenchmarkCleanup, Loc.BenchmarkCleanupConfirm))
        {
            return;
        }

        IsCleaning = true;
        StatusText = Loc.Format("benchmark_cleaning", "正在删除测试 Key…", "Deleting benchmark keys…");
        try
        {
            var deleted = await _benchmark.DeleteTestKeysAsync(BuildSpec());
            StatusText = Loc.Format("benchmark_deleted", "已删除 {0} 个测试 Key", "Deleted {0} benchmark keys", deleted);
            SummaryText = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format("benchmark_cleanup_fail", "清理失败：{0}", "Cleanup failed: {0}", ex.Message);
            await _owner.Prompt.ErrorAsync(Loc.BenchmarkCleanup, ex);
        }
        finally
        {
            IsCleaning = false;
        }
    }

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already finished
        }
    }

    [RelayCommand]
    private Task CloseAsync() => _owner.Workspace.CloseTabCommand.ExecuteAsync(this);

    private async Task<bool> ConfirmStartAsync(BenchmarkSpec spec)
    {
        if (!spec.UseDuration && spec.Requests > BenchmarkLimits.WarnRequests)
        {
            if (!await _owner.Prompt.ConfirmAsync(
                    Loc.Benchmark,
                    Loc.Format(
                        "benchmark_warn_requests",
                        "将发送 {0} 次请求，可能明显增加 Redis 负载。确定继续？",
                        "This will send {0} requests and may load Redis heavily. Continue?",
                        spec.Requests)))
            {
                return false;
            }
        }

        if (spec.UseDuration && spec.DurationSeconds.GetValueOrDefault() > BenchmarkLimits.WarnDurationSeconds)
        {
            if (!await _owner.Prompt.ConfirmAsync(
                    Loc.Benchmark,
                    Loc.Format(
                        "benchmark_warn_duration",
                        "将持续压测 {0} 秒。确定继续？",
                        "This will run for {0} seconds. Continue?",
                        spec.DurationSeconds.GetValueOrDefault())))
            {
                return false;
            }
        }

        return await _owner.Prompt.ConfirmAsync(Loc.Benchmark, Loc.BenchmarkConfirm);
    }

    private BenchmarkSpec BuildSpec()
    {
        var commands = new List<BenchmarkCommandKind>();
        if (IncludePing)
        {
            commands.Add(BenchmarkCommandKind.Ping);
        }

        if (IncludeSet)
        {
            commands.Add(BenchmarkCommandKind.Set);
        }

        if (IncludeGet)
        {
            commands.Add(BenchmarkCommandKind.Get);
        }

        if (IncludeIncr)
        {
            commands.Add(BenchmarkCommandKind.Incr);
        }

        if (IncludeList)
        {
            commands.Add(BenchmarkCommandKind.List);
        }

        if (IncludeHash)
        {
            commands.Add(BenchmarkCommandKind.Hash);
        }

        var secrets = ConnectionSecrets.From(Connection.Config, _owner.Secrets);
        return BenchmarkLimits.Normalize(new BenchmarkSpec(
            Connection.Config,
            secrets,
            commands,
            (int)Requests,
            (int)Concurrency,
            (int)Pipeline,
            (int)ValueBytes,
            KeyPrefix,
            UseDuration ? (int)DurationSeconds : null));
    }

    private void ApplyTick(BenchmarkTick tick)
    {
        QpsPoints = tick.QpsHistory;
        QpsText = FormatNumber(tick.InstantQps);
        P50Text = FormatMs(tick.P50);
        P95Text = FormatMs(tick.P95);
        P99Text = FormatMs(tick.P99);
        MaxText = FormatMs(tick.Max);
        ProgressText = Loc.Format(
            "benchmark_progress",
            "完成 {0}    失败 {1}    {2}s",
            "Done {0}    Fail {1}    {2}s",
            tick.Completed,
            tick.Failed,
            tick.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture));
        SummaryText = Loc.Format(
            "benchmark_summary_live",
            "QPS {0}    p99 {1} ms",
            "QPS {0}    p99 {1} ms",
            tick.OverallQps.ToString("0.#", CultureInfo.InvariantCulture),
            tick.P99.ToString("0.###", CultureInfo.InvariantCulture));
        ReplaceBars(tick.Histogram);
        if (tick.Finished && !string.IsNullOrWhiteSpace(tick.Error))
        {
            StatusText = tick.Error;
        }
    }

    private void ApplyReport(BenchmarkReport report)
    {
        ApplyTick(new BenchmarkTick(
            report.Completed,
            report.Failed,
            report.Bytes,
            report.OverallQps,
            report.OverallQps,
            report.ElapsedSeconds,
            report.P50,
            report.P95,
            report.P99,
            report.Max,
            report.QpsHistory,
            report.Histogram,
            true,
            report.Error));

        if (report.Cancelled)
        {
            StatusText = Loc.BenchmarkCancelled;
            return;
        }

        StatusText = Loc.Format(
            "benchmark_summary_done",
            "完成 {0}/{1}    QPS {2}    吞吐 {3}    p50 {4} / p95 {5} / p99 {6} / max {7} ms",
            "Done {0}/{1}    QPS {2}    {3}    p50 {4} / p95 {5} / p99 {6} / max {7} ms",
            report.Succeeded,
            report.Completed,
            report.OverallQps.ToString("0.#", CultureInfo.InvariantCulture),
            report.ThroughputText,
            report.P50.ToString("0.###", CultureInfo.InvariantCulture),
            report.P95.ToString("0.###", CultureInfo.InvariantCulture),
            report.P99.ToString("0.###", CultureInfo.InvariantCulture),
            report.Max.ToString("0.###", CultureInfo.InvariantCulture));
        SummaryText = StatusText;
    }

    private void ReplaceBars(IReadOnlyList<BenchmarkHistogramBucket> histogram)
    {
        var max = histogram.Count == 0 ? 1 : Math.Max(1, histogram.Max(item => item.Count));
        LatencyBars.Clear();
        foreach (var bucket in histogram)
        {
            LatencyBars.Add(new BenchmarkHistogramBarViewModel(
                bucket.Label,
                bucket.Count,
                8 + 112d * bucket.Count / max));
        }
    }

    private static string FormatNumber(double value)
        => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatMs(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture) + " ms";

    partial void OnIsCleaningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditRequests));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCleanup));
    }
}

public sealed class BenchmarkHistogramBarViewModel(string label, int count, double height)
{
    public string Label { get; } = label;

    public string CountText { get; } = count.ToString(CultureInfo.InvariantCulture);

    public double Height { get; } = height;
}
