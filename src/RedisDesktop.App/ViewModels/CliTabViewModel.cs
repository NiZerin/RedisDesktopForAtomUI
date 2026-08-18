using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class CliTabViewModel : ViewModelBase, IWorkspaceTab
{
    public static readonly string[] Suggestions =
    [
        "PING", "INFO", "GET", "SET", "DEL", "EXISTS", "TTL", "TYPE", "SCAN",
        "HGET", "HSET", "HDEL", "HSCAN", "HLEN",
        "LRANGE", "LPUSH", "RPUSH", "LLEN",
        "SMEMBERS", "SADD", "SREM", "SSCAN", "SCARD",
        "ZRANGE", "ZADD", "ZREM", "ZSCAN", "ZCARD"
    ];

    private readonly MainWindowViewModel _owner;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private string? _lastFullOutput;

    public CliTabViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection)
    {
        _owner = owner;
        Connection = connection;
        Title = $"{connection.Config.Name} CLI";
        StatusText = owner.Loc.T("输入命令后按 Enter 执行。MONITOR 已被拒绝。Ctrl+L 清屏。", "Enter to run. MONITOR is blocked. Ctrl+L clears.");
    }

    public UiStrings Loc => _owner.Loc;

    public string Title { get; }

    public ConnectionItemViewModel Connection { get; }

    public override string ToString() => Title;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private string _statusText = "输入命令后按 Enter 执行。MONITOR 已被拒绝。";

    [ObservableProperty]
    private bool _isBusy;

    public bool CanWrite => !Connection.Config.ReadOnly;

    public IReadOnlyList<string> FilteredSuggestions
    {
        get
        {
            var prefix = Input?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(prefix))
            {
                return Suggestions;
            }

            return Suggestions.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    [RelayCommand]
    public async Task ExecuteAsync()
    {
        var text = Input?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text) || Connection.Session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Remember(text);
            var result = await Connection.Session.Cli.ExecuteAsync(text);
            var header = $"> {text}\n";
            var body = result.Success ? result.Output : (result.Error ?? Loc.T("失败", "Failed"));
            if (result.Truncated)
            {
                body += Loc.T("\n[输出已截断，可复制全文]", "\n[truncated — copy full output]");
            }

            _lastFullOutput = result.FullOutput ?? (result.Success ? result.Output : result.Error);
            Output = header + body + "\n\n" + Output;
            if (Output.Length > CliCommandParser.MaxOutputBytes * 2)
            {
                Output = Output[..CliCommandParser.MaxOutputBytes];
            }

            StatusText = result.Success
                ? (result.Truncated ? Loc.T("已执行（输出截断）", "Done (truncated)") : Loc.T("已执行", "Done"))
                : result.Error ?? Loc.T("失败", "Failed");
            Input = string.Empty;
            OnPropertyChanged(nameof(FilteredSuggestions));
        }
        catch (ReadOnlyException ex)
        {
            StatusText = ex.Message;
            Output = $"> {text}\n{ex.Message}\n\n" + Output;
        }
        catch (Exception ex)
        {
            await _owner.Prompt.ErrorAsync("CLI 执行失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void HistoryUp()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
        Input = _history[_historyIndex];
    }

    public void HistoryDown()
    {
        if (_history.Count == 0 || _historyIndex < 0)
        {
            return;
        }

        _historyIndex++;
        if (_historyIndex >= _history.Count)
        {
            _historyIndex = -1;
            Input = string.Empty;
            return;
        }

        Input = _history[_historyIndex];
    }

    [RelayCommand]
    private async Task CopyOutputAsync()
    {
        await CopyTextAsync(Output);
    }

    [RelayCommand]
    private async Task CopyFullOutputAsync()
    {
        await CopyTextAsync(_lastFullOutput ?? Output);
    }

    public void ClearOutput()
    {
        Output = string.Empty;
        _lastFullOutput = null;
        StatusText = Loc.T("已清屏", "Cleared");
    }

    public void AcceptFirstSuggestion()
    {
        var first = FilteredSuggestions.FirstOrDefault();
        if (!string.IsNullOrEmpty(first))
        {
            Input = first + " ";
        }
    }

    [RelayCommand]
    private void UseSuggestion(string? command)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            Input = command + " ";
        }
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var top = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (top?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            _owner.Prompt.Info(Loc.T("已复制输出", "Copied"));
        }
    }

    [RelayCommand]
    private Task CloseAsync() => _owner.Workspace.CloseTabCommand.ExecuteAsync(this);

    partial void OnInputChanged(string value) => OnPropertyChanged(nameof(FilteredSuggestions));

    private void Remember(string text)
    {
        if (_history.Count == 0 || _history[^1] != text)
        {
            _history.Add(text);
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = -1;
    }
}
