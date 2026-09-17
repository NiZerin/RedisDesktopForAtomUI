using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App.ViewModels;

public partial class MemoryAnalysisTabViewModel : ViewModelBase, IWorkspaceTab
{
    private readonly MainWindowViewModel _owner;
    private readonly List<KeyMemoryUsage> _rows = [];
    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _pauseGate;
    private bool _costDesc = true;
    private int _scanGeneration;

    public MemoryAnalysisTabViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection, string? prefix = null)
    {
        _owner = owner;
        Connection = connection;
        Prefix = prefix?.Trim() ?? string.Empty;
        Title = string.IsNullOrEmpty(Prefix)
            ? $"{connection.Config.Name} {owner.Loc.MemoryAnalysis}"
            : $"{connection.Config.Name} {owner.Loc.MemoryAnalysis} · {Prefix}";
        StatusText = owner.Loc.MemoryAnalysisHint;
        SummaryText = owner.Loc.Format("memory_summary", "合计: {0}    Size: {1}", "Total: {0}    Size: {1}", 0, "0 B");
        FooterText = string.Format(owner.Loc.MaxDisplay, MemoryScanMatch.MaxKeys);
    }

    public UiStrings Loc => _owner.Loc;

    public string Title { get; }

    public ConnectionItemViewModel Connection { get; }

    public string Prefix { get; }

    public override string ToString() => Title;

    public ObservableCollection<MemoryKeyRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    private bool _scanFinished;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private decimal _minSizeKb;

    public bool CanPause => IsScanning && !IsPaused;

    public bool CanResume => IsScanning && IsPaused;

    public bool CanRestart => ScanFinished;

    public bool ShowEmpty => !IsScanning && Rows.Count == 0;

    public bool ShowPrefix => !string.IsNullOrEmpty(Prefix);

    public long TotalSizeBytes { get; private set; }

    [RelayCommand]
    public Task StartAsync() => ScanFinished || _rows.Count == 0 ? RestartAsync() : ResumeAsync();

    [RelayCommand]
    public async Task RestartAsync()
    {
        Cancel();
        _rows.Clear();
        Rows.Clear();
        TotalSizeBytes = 0;
        ScanFinished = false;
        IsPaused = false;
        UpdateSummary();
        await RunScanAsync();
    }

    [RelayCommand]
    public void Pause()
    {
        if (!IsScanning || IsPaused)
        {
            return;
        }

        IsPaused = true;
        _pauseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RebuildView();
    }

    [RelayCommand]
    public Task ResumeAsync()
    {
        if (!IsScanning)
        {
            return ScanFinished ? RestartAsync() : RunScanAsync();
        }

        IsPaused = false;
        _pauseGate?.TrySetResult();
        _pauseGate = null;
        return Task.CompletedTask;
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _scanGeneration);
        _pauseGate?.TrySetResult();
        _pauseGate = null;
        IsPaused = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsScanning = false;
    }

    [RelayCommand]
    private void ToggleSizeOrder()
    {
        if (IsScanning)
        {
            return;
        }

        _costDesc = !_costDesc;
        RebuildView();
    }

    [RelayCommand]
    private async Task OpenKeyAsync(MemoryKeyRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _owner.Workspace.OpenKeyAsync(Connection, row.Key, newTab: true);
    }

    [RelayCommand]
    private Task CloseAsync()
    {
        Cancel();
        return _owner.Workspace.CloseTabCommand.ExecuteAsync(this);
    }

    private async Task RunScanAsync()
    {
        if (Connection.Session is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _scanGeneration);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsScanning = true;
        ScanFinished = false;
        StatusText = Loc.Get("memory_scan_running", "正在扫描并调用 MEMORY USAGE，可能影响延迟。", "Scanning with MEMORY USAGE. This may affect latency.");
        try
        {
            var session = Connection.Session;
            var match = MemoryScanMatch.FromPrefix(Prefix);
            var minBytes = (long)(MinSizeKb * 1024);
            long cursor = 0;
            var exhausted = false;
            var hitMax = false;
            while (!exhausted && !token.IsCancellationRequested)
            {
                await WaitIfPausedAsync(token);
                if (token.IsCancellationRequested || generation != _scanGeneration)
                {
                    return;
                }

                var page = await session.Keys.ScanAsync(
                    new ScanRequest(session.CurrentDatabase, match, cursor, MemoryScanMatch.DefaultPageSize),
                    log: false,
                    token);
                var usages = await session.Observability.GetMemoryUsageAsync(page.Keys, minBytes, token);
                if (generation != _scanGeneration)
                {
                    return;
                }

                var start = _rows.Count;
                foreach (var usage in usages)
                {
                    _rows.Add(usage);
                    TotalSizeBytes += Math.Max(0, usage.SizeBytes);
                    if (_rows.Count >= MemoryScanMatch.MaxKeys)
                    {
                        hitMax = true;
                        break;
                    }
                }

                for (var i = start; i < _rows.Count; i++)
                {
                    Rows.Add(new MemoryKeyRowViewModel(i + 1, _rows[i]));
                }

                UpdateSummary();
                cursor = page.Cursor;
                exhausted = page.Exhausted;
                if (hitMax)
                {
                    break;
                }

                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }

            ApplyOrderToStore();
            RebuildView();
            ScanFinished = true;
            StatusText = hitMax
                ? string.Format(Loc.MaxScan, MemoryScanMatch.MaxKeys)
                : Loc.Get("scan_finished", "扫描完成", "Scan finished");
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("stopped", "已停止", "Stopped");
        }
        catch (Exception ex)
        {
            AppLog.Error("MemoryAnalysis", ex);
            StatusText = ex.Message;
            await _owner.Prompt.ErrorAsync(Loc.MemoryAnalysis, ex);
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                IsPaused = false;
                ScanFinished = true;
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken token)
    {
        var gate = _pauseGate;
        if (gate is null)
        {
            return;
        }

        await gate.Task.WaitAsync(token);
    }

    private void ApplyOrderToStore()
    {
        _rows.Sort((left, right) => _costDesc
            ? right.SizeBytes.CompareTo(left.SizeBytes)
            : left.SizeBytes.CompareTo(right.SizeBytes));
    }

    private void RebuildView()
    {
        ApplyOrderToStore();
        Rows.Clear();
        var index = 1;
        foreach (var usage in _rows.Take(MemoryScanMatch.MaxKeys))
        {
            Rows.Add(new MemoryKeyRowViewModel(index++, usage));
        }

        UpdateSummary();
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void UpdateSummary()
    {
        SummaryText = Loc.Format(
            "memory_summary",
            "合计: {0}    Size: {1}",
            "Total: {0}    Size: {1}",
            _rows.Count,
            ValueViewerPipeline.FormatByteSize(TotalSizeBytes));
    }
}

public sealed class MemoryKeyRowViewModel(int index, KeyMemoryUsage usage)
{
    public int Index { get; } = index;

    public RedisKeyBytes Key { get; } = usage.Key;

    public string Display { get; } = usage.Display;

    public string SizeText { get; } = usage.SizeText;

    public long SizeBytes { get; } = usage.SizeBytes;
}
