using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class ConnectionWorkspaceViewModel : ViewModelBase, IWorkspaceTab
{
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly List<InfoRowViewModel> _allInfoRows = [];
    private int _refreshing;

    public ConnectionWorkspaceViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection)
    {
        Owner = owner;
        Connection = connection;
        Browser = new KeyBrowserViewModel(this);
        Title = connection.Config.Name;
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoRefreshTimer.Tick += (_, _) => _ = RefreshStatusAsync(showError: false);
    }

    public MainWindowViewModel Owner { get; }

    public UiStrings Loc => Owner.Loc;

    public ConnectionItemViewModel Connection { get; }

    public KeyBrowserViewModel Browser { get; }

    public string Title { get; }

    public ObservableCollection<KeyspaceRowViewModel> Keyspace { get; } = [];

    public ObservableCollection<InfoRowViewModel> InfoRows { get; } = [];

    public bool CanWrite => !IsReadOnly;

    [ObservableProperty]
    private string _statusSummary = string.Empty;

    [ObservableProperty]
    private string _redisVersion = "-";

    [ObservableProperty]
    private string _os = "-";

    [ObservableProperty]
    private string _processId = "-";

    [ObservableProperty]
    private string _usedMemory = "-";

    [ObservableProperty]
    private string _usedMemoryPeak = "-";

    [ObservableProperty]
    private string _usedMemoryLua = "-";

    [ObservableProperty]
    private string _connectedClients = "-";

    [ObservableProperty]
    private string _totalConnections = "-";

    [ObservableProperty]
    private string _totalCommands = "-";

    [ObservableProperty]
    private string _infoFilter = string.Empty;

    [ObservableProperty]
    private bool _autoRefresh;

    [ObservableProperty]
    private bool _isCluster;

    public IRedisSession Session => Connection.Session
        ?? throw new ConnectException("连接已断开。");

    public bool IsReadOnly => Connection.Config.ReadOnly;

    public void Cancel()
    {
        Browser.Cancel();
        PauseAutoRefresh();
    }

    public void PauseAutoRefresh() => _autoRefreshTimer.Stop();

    public void ResumeStatus()
    {
        if (AutoRefresh)
        {
            _autoRefreshTimer.Start();
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            await RefreshStatusAsync(showError: true);
            await Browser.SearchAsync();
        }
        catch (Exception ex)
        {
            Owner.StatusText = Owner.Loc.T($"刷新失败：{ex.Message}", $"Refresh failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task RefreshStatusAsync() => RefreshStatusAsync(showError: true);

    private async Task RefreshStatusAsync(bool showError)
    {
        if (Connection.Session is null)
        {
            StatusSummary = string.Empty;
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var ping = await Connection.Session.PingAsync();
            var size = await Connection.Session.GetDbSizeAsync();
            var info = await Connection.Session.GetInfoAsync(null);
            var status = RedisInfoParser.Parse(info);
            ApplyServerStatus(status);
            await ApplyKeyspaceAsync(status);
            RebuildInfoRows(status);
            var mode = RedisInfoParser.Get(status, "redis_mode", Connection.Config.Kind.ToString());
            StatusSummary = $"redis {RedisVersion} · {mode} · db{Connection.Session.CurrentDatabase} · {size} keys · ping {ping}";
        }
        catch (Exception ex)
        {
            StatusSummary = ex.Message;
            if (showError)
            {
                await ShowInfoErrorAsync(ex);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    [RelayCommand]
    private Task ShowHomeAsync()
    {
        Owner.Workspace.Open(Connection);
        return RefreshStatusAsync(showError: true);
    }

    [RelayCommand]
    private async Task NewKeyAsync()
    {
        if (IsReadOnly)
        {
            return;
        }

        var dialog = new NewKeyDialogViewModel(Loc);
        if (!await Owner.Prompt.ShowNewKeyDialogAsync(dialog))
        {
            return;
        }

        var name = dialog.KeyName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var key = RedisKeyBytes.FromUtf8(name);
        var type = dialog.SelectedType?.Type ?? RedisKeyType.String;
        try
        {
            await WriteNewKeyDefaultAsync(key, type);
            Browser.AddScannedKey(key);
            await Owner.Workspace.OpenKeyAsync(Connection, key, newTab: true);
        }
        catch (Exception ex)
        {
            await Owner.Prompt.ErrorAsync(ex.Message, ex);
        }
    }

    private Task WriteNewKeyDefaultAsync(RedisKeyBytes key, RedisKeyType type)
        => type switch
        {
            RedisKeyType.Hash => Session.Values.HashSetAsync(key, "New field", "New value", null),
            RedisKeyType.List => Session.Values.ListPushAsync(key, "New member", left: true),
            RedisKeyType.Set => Session.Values.SetAddAsync(key, "New member"),
            RedisKeyType.SortedSet => Session.Values.ZAddAsync(key, "New member", 0),
            RedisKeyType.Stream => Session.Values.StreamAddAsync(key, [new StreamField("New key", "New value")]),
            _ => Session.Values.SaveStringAsync(key, [], null)
        };

    [RelayCommand]
    private Task CloseAsync() => Owner.Workspace.CloseTabCommand.ExecuteAsync(this);

    [RelayCommand]
    private Task OpenCliAsync() => Owner.Workspace.OpenCliAsync(Connection);

    [RelayCommand]
    private Task OpenPubSubAsync() => Owner.Workspace.OpenPubSubAsync(Connection);

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value)
        {
            _autoRefreshTimer.Start();
            _ = RefreshStatusAsync(showError: false);
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    partial void OnInfoFilterChanged(string value) => ApplyInfoFilter();

    private void ApplyServerStatus(IReadOnlyDictionary<string, string> status)
    {
        RedisVersion = RedisInfoParser.Get(status, "redis_version");
        Os = RedisInfoParser.Get(status, "os");
        ProcessId = RedisInfoParser.Get(status, "process_id");
        UsedMemory = RedisInfoParser.FormatBytes(RedisInfoParser.Get(status, "used_memory", string.Empty));
        UsedMemoryPeak = RedisInfoParser.FormatBytes(RedisInfoParser.Get(status, "used_memory_peak", string.Empty));
        var lua = RedisInfoParser.Get(status, "used_memory_lua", string.Empty);
        if (string.IsNullOrWhiteSpace(lua) || lua == "-")
        {
            lua = RedisInfoParser.Get(status, "used_memory_scripts", string.Empty);
        }

        UsedMemoryLua = RedisInfoParser.FormatBytes(lua);
        ConnectedClients = RedisInfoParser.FormatNumber(RedisInfoParser.Get(status, "connected_clients", string.Empty));
        TotalConnections = RedisInfoParser.FormatNumber(RedisInfoParser.Get(status, "total_connections_received", string.Empty));
        TotalCommands = RedisInfoParser.FormatNumber(RedisInfoParser.Get(status, "total_commands_processed", string.Empty));
        IsCluster = RedisInfoParser.IsClusterEnabled(status);
    }

    private async Task ApplyKeyspaceAsync(IReadOnlyDictionary<string, string> status)
    {
        if (IsCluster && Connection.Session is not null)
        {
            try
            {
                var nodes = await Connection.Session.GetMasterInfoAsync("keyspace");
                var rows = new List<RedisInfoKeyspaceRow>();
                foreach (var node in nodes)
                {
                    rows.AddRange(RedisInfoParser.ParseKeyspace(RedisInfoParser.Parse(node.Content), node.Node));
                }

                if (rows.Count > 0)
                {
                    ReplaceKeyspace(rows);
                    return;
                }
            }
            catch
            {
                // Fall back to the current node's INFO KEYSPACE.
            }
        }

        ReplaceKeyspace(RedisInfoParser.ParseKeyspace(status));
    }

    private void ReplaceKeyspace(IReadOnlyList<RedisInfoKeyspaceRow> rows)
    {
        Keyspace.Clear();
        foreach (var row in rows
                     .OrderBy(r => r.Node ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.Database, StringComparer.OrdinalIgnoreCase))
        {
            Keyspace.Add(new KeyspaceRowViewModel
            {
                Node = row.Node ?? string.Empty,
                Database = row.Database,
                Keys = RedisInfoParser.FormatNumber(row.Keys),
                KeysValue = row.Keys,
                Expires = RedisInfoParser.FormatNumber(row.Expires),
                ExpiresValue = row.Expires,
                AvgTtl = RedisInfoParser.FormatNumber(row.AvgTtl),
                AvgTtlValue = row.AvgTtl
            });
        }

        Connection.ApplyDatabaseKeyCounts(rows);
    }

    private void RebuildInfoRows(IReadOnlyDictionary<string, string> status)
    {
        _allInfoRows.Clear();
        foreach (var (key, value) in status)
        {
            _allInfoRows.Add(new InfoRowViewModel { Key = key, Value = value });
        }

        _allInfoRows.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
        ApplyInfoFilter();
    }

    private void ApplyInfoFilter()
    {
        InfoRows.Clear();
        var filter = InfoFilter.Trim();
        foreach (var row in _allInfoRows)
        {
            if (filter.Length == 0 || row.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                InfoRows.Add(row);
            }
        }
    }

    private async Task ShowInfoErrorAsync(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            await Owner.Prompt.ErrorAsync(Loc.InfoDisabled, ex);
            return;
        }

        await Owner.Prompt.ErrorAsync(message, ex);
    }

    public override string ToString() => Title;
}

public sealed class KeyspaceRowViewModel
{
    public string Node { get; init; } = string.Empty;

    public required string Database { get; init; }

    public required string Keys { get; init; }

    public long KeysValue { get; init; }

    public required string Expires { get; init; }

    public long ExpiresValue { get; init; }

    public required string AvgTtl { get; init; }

    public long AvgTtlValue { get; init; }
}

public sealed class InfoRowViewModel
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}
