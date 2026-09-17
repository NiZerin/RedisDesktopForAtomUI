using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class AsideViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;
    private readonly List<ConnectionItemViewModel> _all = [];

    public AsideViewModel(MainWindowViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<IConnectionTreeNode> Nodes { get; } = [];

    public ObservableCollection<ConnectionItemViewModel> ConnectionList { get; } = [];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private IConnectionTreeNode? _selectedNode;

    public bool ShowFilter => _all.Count >= 4;

    public void Load(IReadOnlyList<ConnectionConfig> configs)
    {
        _all.Clear();
        foreach (var config in configs)
        {
            _all.Add(new ConnectionItemViewModel(_owner, config));
        }

        Rebuild();
    }

    public void Add(ConnectionConfig config)
    {
        var item = new ConnectionItemViewModel(_owner, config);
        _all.Add(item);
        Rebuild();
        SelectedConnection = item;
    }

    public IReadOnlyList<ConnectionConfig> GetConfigs() => _all.Select(x => x.Config).ToList();

    public void Rebuild()
    {
        Nodes.Clear();
        ConnectionList.Clear();
        var filter = Filter?.Trim() ?? string.Empty;
        IEnumerable<ConnectionItemViewModel> items = _all;
        if (!string.IsNullOrEmpty(filter))
        {
            items = items.Where(x =>
                x.Config.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Config.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (x.Config.GroupName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = items.ToList();
        foreach (var item in list.OrderBy(x => x.Config.GroupName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            ConnectionList.Add(item);
        }

        foreach (var group in list.GroupBy(x => string.IsNullOrWhiteSpace(x.Config.GroupName) ? "" : x.Config.GroupName!.Trim())
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var item in group.OrderBy(x => x.Config.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Nodes.Add(item);
                }
            }
            else
            {
                var node = new ConnectionGroupViewModel(group.Key);
                foreach (var item in group.OrderBy(x => x.Config.Name, StringComparer.OrdinalIgnoreCase))
                {
                    node.Children.Add(item);
                }

                Nodes.Add(node);
            }
        }

        OnPropertyChanged(nameof(ShowFilter));
        ConnectionCount = ConnectionList.Count;
        IsEmpty = ConnectionList.Count == 0;
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ListSummary));
        OnPropertyChanged(nameof(HasSelectedConnection));
    }

    public bool HasConnections => ConnectionList.Count > 0;

    public bool HasSelectedConnection => SelectedConnection is not null;

    public bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    private int _connectionCount;

    public string ListSummary => Loc.T($"连接列表（{ConnectionCount}）", $"Connections ({ConnectionCount})");

    public void Remove(ConnectionItemViewModel item)
    {
        _all.Remove(item);
        Rebuild();
    }

    public void StopAllHeartbeats()
    {
        foreach (var item in _all)
        {
            item.StopHeartbeat();
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var item in _all.ToList())
        {
            if (item.Session is not null)
            {
                await item.DetachSessionAsync();
            }
        }
    }

    partial void OnFilterChanged(string value) => Rebuild();

    [RelayCommand]
    private Task ConnectAsync() => SelectedConnection is { } item ? _owner.ConnectAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private async Task OpenSelectedAsync()
    {
        if (SelectedNode is DatabaseNodeViewModel db)
        {
            await _owner.SwitchDatabaseAsync(db);
            return;
        }

        if (SelectedConnection is { } item)
        {
            await _owner.ConnectAsync(item);
        }
    }

    [RelayCommand]
    private Task DisconnectAsync() => SelectedConnection is { } item ? _owner.DisconnectAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() => SelectedConnection is { } item ? _owner.EditConnectionAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private async Task CloneAsync()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        var clone = SelectedConnection.Config.Clone();
        _owner.Aside.Add(clone);
        await _owner.SaveConnectionsAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        if (!await _owner.Prompt.ConfirmAsync(_owner.Loc.DeleteConnection, $"确定删除连接「{SelectedConnection.Config.Name}」？"))
        {
            return;
        }

        await _owner.DisconnectAsync(SelectedConnection);
        Remove(SelectedConnection);
        await _owner.SaveConnectionsAsync();
    }

    public ConnectionItemViewModel? SelectedConnection
    {
        get => _selectedConnection ?? SelectedNode as ConnectionItemViewModel
            ?? (SelectedNode as DatabaseNodeViewModel)?.Connection;
        set
        {
            if (SetProperty(ref _selectedConnection, value))
            {
                SelectedNode = value;
            }
        }
    }

    private ConnectionItemViewModel? _selectedConnection;

    partial void OnSelectedNodeChanged(IConnectionTreeNode? value)
    {
        if (value is ConnectionItemViewModel item)
        {
            _selectedConnection = item;
        }
        else if (value is DatabaseNodeViewModel db)
        {
            _selectedConnection = db.Connection;
        }
        else
        {
            _selectedConnection = null;
        }

        OnPropertyChanged(nameof(SelectedConnection));
        OnPropertyChanged(nameof(HasSelectedConnection));
    }

    public UiStrings Loc => _owner.Loc;

    [RelayCommand]
    private async Task MoveToGroupAsync()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        var name = await _owner.Prompt.PromptTextAsync(Loc.MoveToGroup, Loc.GroupPrompt, SelectedConnection.Config.GroupName ?? string.Empty);
        if (name is null)
        {
            return;
        }

        SelectedConnection.Config.GroupName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Rebuild();
        await _owner.SaveConnectionsAsync();
    }

    [RelayCommand]
    private Task NewConnectionAsync() => _owner.NewConnectionCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task OpenCliAsync() => SelectedConnection is { } item ? _owner.OpenCliAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenPubSubAsync() => SelectedConnection is { } item ? _owner.OpenPubSubAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private Task ImportConnectionsAsync() => _owner.ImportConnectionsCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ExportConnectionsAsync() => _owner.ExportConnectionsCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task OpenCommandLogAsync() => _owner.ToggleCommandLogCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task OpenSettingsAsync() => _owner.OpenSettingsCommand.ExecuteAsync(null);
}

public interface IConnectionTreeNode
{
    string Title { get; }

    string Subtitle { get; }

    bool HasSubtitle { get; }

    IEnumerable<IConnectionTreeNode>? TreeChildren { get; }

    bool HasColor { get; }

    IBrush MarkerBrush { get; }
}

public sealed partial class ConnectionGroupViewModel : ViewModelBase, IConnectionTreeNode
{
    public ConnectionGroupViewModel(string name)
    {
        Title = name;
    }

    public string Title { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public string Subtitle => string.Empty;

    public bool HasSubtitle => false;

    public ObservableCollection<IConnectionTreeNode> Children { get; } = [];

    public IEnumerable<IConnectionTreeNode>? TreeChildren => Children.Count == 0 ? null : Children;

    public bool HasColor => false;

    public IBrush MarkerBrush => Brushes.Transparent;

    public override string ToString() => Title;
}

public partial class ConnectionItemViewModel : ViewModelBase, IConnectionTreeNode
{
    private readonly MainWindowViewModel _owner;
    private CancellationTokenSource? _heartbeat;

    public ConnectionItemViewModel(MainWindowViewModel owner, ConnectionConfig config)
    {
        _owner = owner;
        Config = config;
        Title = FormatTitle(config);
        StatusText = FormatEndpoint(config);
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TreeChildren));
            OnPropertyChanged(nameof(HasDatabases));
        };
    }

    public ConnectionConfig Config { get; private set; }

    public string Title { get; private set; }

    public string Subtitle => Endpoint;

    public bool HasSubtitle => true;

    public ObservableCollection<IConnectionTreeNode> Children { get; } = [];

    public IEnumerable<IConnectionTreeNode>? TreeChildren => Children.Count == 0 ? null : Children;

    public bool HasDatabases => Children.Count > 0;

    public bool HasColor => ConnectionColors.TryGetHex(Config.ColorTag, out _);

    public IBrush MarkerBrush => ConnectionColors.ToBrush(Config.ColorTag);

    public Thickness ColorBarThickness => HasColor ? new Thickness(5, 0, 0, 0) : default;

    [ObservableProperty]
    private SessionState _state = SessionState.Idle;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public IRedisSession? Session { get; private set; }

    public ConnectionWorkspaceViewModel? StatusHost { get; private set; }

    public KeyBrowserViewModel? Browser => StatusHost?.Browser;

    public bool ShowOperatePanel => IsConnected && StatusHost is not null;

    public bool ShowDatabasePicker => ShowOperatePanel && HasDatabases;

    public bool CanWrite => IsConnected && !Config.ReadOnly;

    public bool CanBenchmark => !Config.ReadOnly;

    public string Endpoint => FormatEndpoint(Config);

    public string DisplayHost => $"{Config.Host}@{Config.Port}";

    public bool IsConnected => State == SessionState.Connected;

    public bool IsDisconnected => State != SessionState.Connected;

    public ConnectionWorkspaceViewModel? Workspace => StatusHost;

    [ObservableProperty]
    private DatabaseNodeViewModel? _selectedDatabase;

    public void ReplaceConfig(ConnectionConfig config)
    {
        Config = config;
        Title = FormatTitle(config);
        StatusText = Endpoint;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(DisplayHost));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(MarkerBrush));
        OnPropertyChanged(nameof(ColorBarThickness));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanBenchmark));
    }

    public void NotifyEndpointChanged()
    {
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Subtitle));
        if (State != SessionState.Connected)
        {
            StatusText = Endpoint;
        }
    }

    public void AttachSession(IRedisSession session)
    {
        Session = session;
        State = SessionState.Connected;
        StatusText = _owner.Loc.Connected;
        StatusHost = new ConnectionWorkspaceViewModel(_owner, this);
        NotifyOperatePanel();
        StartHeartbeat();
        _ = LoadDatabasesAsync();
    }

    public async Task DetachSessionAsync()
    {
        StopHeartbeat();
        StatusHost?.Cancel();
        StatusHost = null;
        IsExpanded = false;
        Children.Clear();
        if (Session is not null)
        {
            await Session.DisposeAsync();
            Session = null;
        }

        State = SessionState.Idle;
        StatusText = Endpoint;
        NotifyOperatePanel();
    }

    public async Task LoadDatabasesAsync()
    {
        if (Session is null || Session.Config.Kind == RedisDeploymentKind.Cluster)
        {
            Children.Clear();
            return;
        }

        var count = 16;
        try
        {
            count = await Session.GetDatabaseCountAsync();
        }
        catch
        {
            // keep default
        }

        Children.Clear();
        var current = Session.CurrentDatabase;
        var safeCount = Math.Clamp(count, 1, 64);
        for (var i = 0; i < safeCount; i++)
        {
            Children.Add(new DatabaseNodeViewModel(this, i, i == current));
        }

        await ApplyKeyspaceCountsAsync();
        OnPropertyChanged(nameof(TreeChildren));
        OnPropertyChanged(nameof(HasDatabases));
        OnPropertyChanged(nameof(ShowDatabasePicker));
        SelectedDatabase = Children.OfType<DatabaseNodeViewModel>().FirstOrDefault(x => x.IsActive);
    }

    public void ApplyDatabaseKeyCounts(IReadOnlyList<RedisInfoKeyspaceRow> rows)
    {
        var counts = new Dictionary<int, long>();
        var maxIndex = -1;
        foreach (var row in rows)
        {
            if (!RedisInfoParser.TryGetDatabaseIndex(row.Database, out var index))
            {
                continue;
            }

            counts[index] = row.Keys;
            if (index > maxIndex)
            {
                maxIndex = index;
            }
        }

        if (Session is not null
            && Session.Config.Kind != RedisDeploymentKind.Cluster
            && Children.Count > 0
            && maxIndex >= Children.Count)
        {
            var current = Session.CurrentDatabase;
            var target = Math.Clamp(maxIndex + 1, Children.Count, 64);
            for (var i = Children.Count; i < target; i++)
            {
                Children.Add(new DatabaseNodeViewModel(this, i, i == current));
            }
        }

        foreach (var child in Children.OfType<DatabaseNodeViewModel>())
        {
            child.KeyCount = counts.TryGetValue(child.Index, out var keys) ? keys : null;
        }
    }

    private async Task ApplyKeyspaceCountsAsync()
    {
        if (Session is null)
        {
            return;
        }

        try
        {
            var info = await Session.GetInfoAsync("keyspace");
            ApplyDatabaseKeyCounts(RedisInfoParser.ParseKeyspace(RedisInfoParser.Parse(info)));
        }
        catch
        {
            // INFO may be disabled; the DB list still works without counts.
        }
    }

    public void NotifyWorkspace()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(Workspace));
        NotifyOperatePanel();
    }

    public void SelectSelf() => _owner.Aside.SelectedConnection = this;

    private void NotifyOperatePanel()
    {
        OnPropertyChanged(nameof(StatusHost));
        OnPropertyChanged(nameof(Browser));
        OnPropertyChanged(nameof(ShowOperatePanel));
        OnPropertyChanged(nameof(ShowDatabasePicker));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanBenchmark));
    }

    partial void OnStateChanged(SessionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(Workspace));
        NotifyOperatePanel();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        SelectSelf();
        if (IsDisconnected)
        {
            _ = _owner.ConnectAsync(this);
        }
    }

    partial void OnSelectedDatabaseChanged(DatabaseNodeViewModel? value)
    {
        if (value is null || Session is null || Session.CurrentDatabase == value.Index)
        {
            return;
        }

        _ = OpenDatabaseAsync(value);
    }

    public void MarkActiveDatabase(int index)
    {
        foreach (var child in Children.OfType<DatabaseNodeViewModel>())
        {
            child.IsActive = child.Index == index;
        }
    }

    public UiStrings Loc => _owner.Loc;

    [RelayCommand]
    private Task OpenAsync() => _owner.ConnectAsync(this);

    [RelayCommand]
    private async Task EditAsync()
    {
        if (IsConnected)
        {
            if (!await _owner.Prompt.ConfirmAsync(Loc.EditConnection, Loc.CloseToEditConnection))
            {
                return;
            }

            await _owner.DisconnectAsync(this);
        }

        await _owner.EditConnectionAsync(this);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        if (!await _owner.Prompt.ConfirmAsync(Loc.CloseConnection, Loc.CloseToConnection))
        {
            return;
        }

        await _owner.DisconnectAsync(this);
    }

    [RelayCommand]
    private Task ShowHomeAsync()
    {
        if (StatusHost is null)
        {
            return _owner.ConnectAsync(this);
        }

        _owner.Workspace.Open(this);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshConnectionAsync()
        => StatusHost is null ? _owner.ConnectAsync(this) : StatusHost.RefreshAsync();

    [RelayCommand]
    private Task NewKeyAsync()
    {
        var tab = StatusHost;
        return tab is null ? Task.CompletedTask : tab.NewKeyCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task OpenCliAsync() => _owner.OpenCliAsync(this);

    [RelayCommand]
    private Task OpenPubSubAsync() => _owner.OpenPubSubAsync(this);

    [RelayCommand]
    private Task OpenSlowLogAsync() => _owner.OpenSlowLogAsync(this);

    [RelayCommand]
    private Task OpenMemoryAnalysisAsync() => _owner.OpenMemoryAnalysisAsync(this);

    [RelayCommand]
    private Task OpenBenchmarkAsync() => _owner.OpenBenchmarkAsync(this);

    [RelayCommand]
    private async Task CloneAsync()
    {
        var clone = Config.Clone();
        _owner.Aside.Add(clone);
        await _owner.SaveConnectionsAsync();
        if (_owner.Aside.SelectedConnection is { } created)
        {
            await _owner.EditConnectionAsync(created);
        }
    }

    [RelayCommand]
    private async Task UseColorAsync(string? tag)
    {
        Config.ColorTag = string.IsNullOrWhiteSpace(tag) || tag is "none"
            ? null
            : tag.Trim().ToLowerInvariant();
        ReplaceConfig(Config);
        await _owner.SaveConnectionsAsync();
    }

    [RelayCommand]
    private async Task FlushDbAsync()
    {
        if (Session is null || StatusHost is null)
        {
            return;
        }

        if (Config.ReadOnly)
        {
            await _owner.Prompt.ErrorAsync(Loc.ReadonlyHint);
            return;
        }

        var db = Session.CurrentDatabase;
        var typed = await _owner.Prompt.PromptTextAsync(
            Loc.FlushDb,
            Loc.T($"确认清空当前 DB{db}？{Loc.FlushDbPrompt}", $"Flush current DB{db}? {Loc.FlushDbPrompt}"),
            string.Empty);
        if (!string.Equals(typed?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var result = await Session.Cli.ExecuteAsync("FLUSHDB");
            if (!result.Success)
            {
                await _owner.Prompt.ErrorAsync(result.Error ?? "FLUSHDB failed");
                return;
            }

            await StatusHost.RefreshAsync();
            _owner.StatusText = Loc.T($"已清空 DB{db}", $"Flushed DB{db}");
        }
        catch (Exception ex)
        {
            await _owner.Prompt.ErrorAsync("FLUSHDB 失败", ex);
        }
    }

    [RelayCommand]
    private async Task MoveToGroupAsync()
    {
        var name = await _owner.Prompt.PromptTextAsync(Loc.MoveToGroup, Loc.GroupPrompt, Config.GroupName ?? string.Empty);
        if (name is null)
        {
            return;
        }

        Config.GroupName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        _owner.Aside.Rebuild();
        await _owner.SaveConnectionsAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!await _owner.Prompt.ConfirmAsync(_owner.Loc.DeleteConnection, $"确定删除连接「{Config.Name}」？"))
        {
            return;
        }

        await _owner.DisconnectAsync(this);
        _owner.Aside.Remove(this);
        await _owner.SaveConnectionsAsync();
    }

    public Task OpenDatabaseAsync(DatabaseNodeViewModel node) => _owner.SwitchDatabaseAsync(node);

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeat = new CancellationTokenSource();
        var token = _heartbeat.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                    if (Session is null)
                    {
                        return;
                    }

                    if (!await Session.TryHeartbeatAsync(token))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (State == SessionState.Connected)
                            {
                                State = SessionState.Failed;
                                StatusText = _owner.Loc.HeartbeatLost;
                            }
                        });
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    return;
                }
            }
        }, token);
    }

    public void StopHeartbeat()
    {
        _heartbeat?.Cancel();
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    private static string FormatTitle(ConnectionConfig config)
        => string.IsNullOrWhiteSpace(config.DatabaseAlias)
            ? config.Name
            : $"{config.Name} ({config.DatabaseAlias})";

    private static string FormatEndpoint(ConnectionConfig config)
    {
        var alias = string.IsNullOrWhiteSpace(config.DatabaseAlias) ? string.Empty : $" {config.DatabaseAlias}";
        return $"{config.Host}:{config.Port} / db{config.Database}{alias}";
    }

    public override string ToString() => Title;
}

public partial class DatabaseNodeViewModel : ViewModelBase, IConnectionTreeNode
{
    public DatabaseNodeViewModel(ConnectionItemViewModel connection, int index, bool active)
    {
        Connection = connection;
        Index = index;
        IsActive = active;
    }

    public ConnectionItemViewModel Connection { get; }

    public int Index { get; }

    public string Name => $"DB{Index}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComboTitle))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(HasKeyCount))]
    [NotifyPropertyChangedFor(nameof(KeyCountText))]
    private long? _keyCount;

    public bool HasKeyCount => KeyCount.HasValue;

    public string KeyCountText => KeyCount is { } count ? $"({count})" : string.Empty;

    public string ComboTitle => HasKeyCount ? $"DB{Index} {KeyCountText}" : $"DB{Index}";

    public string Title => IsActive ? $"{ComboTitle} *" : ComboTitle;

    public string Subtitle => string.Empty;

    public bool HasSubtitle => false;

    public IEnumerable<IConnectionTreeNode>? TreeChildren => null;

    public bool HasColor => Connection.HasColor;

    public IBrush MarkerBrush => Connection.MarkerBrush;

    [RelayCommand]
    private Task OpenAsync() => Connection.OpenDatabaseAsync(this);

    public override string ToString() => ComboTitle;
}

public static class ConnectionColors
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#E74C3C",
        ["blue"] = "#3498DB",
        ["green"] = "#27AE60",
        ["orange"] = "#E67E22",
        ["purple"] = "#8E44AD"
    };

    public static bool TryGetHex(string? tag, out string hex)
    {
        if (!string.IsNullOrWhiteSpace(tag) && Map.TryGetValue(tag.Trim(), out hex!))
        {
            return true;
        }

        hex = "#00000000";
        return false;
    }

    public static IBrush ToBrush(string? tag)
        => TryGetHex(tag, out var hex)
            ? new SolidColorBrush(Color.Parse(hex))
            : Brushes.Transparent;
}
