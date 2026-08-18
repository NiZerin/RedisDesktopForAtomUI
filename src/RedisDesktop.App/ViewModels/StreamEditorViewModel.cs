using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class StreamEditorViewModel : ViewModelBase
{
    private readonly ConnectionWorkspaceViewModel _workspace;
    private RedisKeyBytes _key = RedisKeyBytes.FromUtf8(string.Empty);
    private string _pageStart = "-";
    private string _nextStart = "-";
    private readonly Stack<string> _history = new();

    public StreamEditorViewModel(ConnectionWorkspaceViewModel workspace)
    {
        _workspace = workspace;
    }

    public UiStrings Loc => _workspace.Owner.Loc;

    public ObservableCollection<StreamEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private int _pageSize = 50;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canGoPrev;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private string _newFields = "field1:value1";

    [ObservableProperty]
    private bool _isBusy;

    public bool CanWrite => !_workspace.IsReadOnly;

    public async Task LoadAsync(RedisKeyBytes key)
    {
        _key = key;
        _pageStart = "-";
        _nextStart = "-";
        _history.Clear();
        await LoadPageAsync(reset: true);
    }

    public void Clear()
    {
        Entries.Clear();
        StatusText = string.Empty;
        _history.Clear();
        CanGoPrev = false;
        CanGoNext = false;
        _nextStart = "-";
    }

    [RelayCommand]
    private Task RefreshPageAsync() => LoadPageAsync(reset: true);

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        _history.Push(_pageStart);
        _pageStart = _nextStart;
        await LoadPageAsync(reset: false);
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _pageStart = _history.Pop();
        await LoadPageAsync(reset: false);
        CanGoPrev = _history.Count > 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite)
        {
            return;
        }

        var fields = ParseFields(NewFields);
        if (fields.Count == 0)
        {
            await _workspace.Owner.Prompt.ErrorAsync("请按 field:value 填写，多个字段用逗号或换行分隔。");
            return;
        }

        try
        {
            await _workspace.Session.Values.StreamAddAsync(_key, fields);
            await LoadPageAsync(reset: true);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("XADD 失败", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(StreamEntryViewModel? entry)
    {
        if (entry is null || !CanWrite)
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync("删除 Stream 条目", $"确定删除 {entry.Id}？"))
        {
            return;
        }

        try
        {
            await _workspace.Session.Values.StreamDeleteAsync(_key, entry.Id);
            await LoadPageAsync(reset: true);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("XDEL 失败", ex);
        }
    }

    private async Task LoadPageAsync(bool reset)
    {
        IsBusy = true;
        try
        {
            if (reset)
            {
                _pageStart = "-";
                _nextStart = "-";
                _history.Clear();
                CanGoPrev = false;
            }

            var page = await _workspace.Session.Values.RangeStreamAsync(_key, _pageStart, Math.Clamp(PageSize, 10, 500));
            Entries.Clear();
            foreach (var item in page.Items)
            {
                Entries.Add(new StreamEntryViewModel(item, this));
            }

            _nextStart = page.NextStartId;
            CanGoNext = !page.Exhausted;
            CanGoPrev = _history.Count > 0;
            var total = page.TotalCount is { } t ? $" / 共 {t}" : string.Empty;
            StatusText = $"已加载 {page.LoadedCount}{total}（XRANGE，删除需确认）";
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("加载 Stream 失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<StreamField> ParseFields(string text)
    {
        var fields = new List<StreamField>();
        foreach (var part in text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.IndexOf(':');
            if (split <= 0)
            {
                continue;
            }

            fields.Add(new StreamField(part[..split].Trim(), part[(split + 1)..].Trim()));
        }

        return fields;
    }
}

public partial class StreamEntryViewModel : ViewModelBase
{
    private readonly StreamEditorViewModel _owner;

    public StreamEntryViewModel(StreamEntry entry, StreamEditorViewModel owner)
    {
        _owner = owner;
        Id = entry.Id;
        Body = string.Join("  ", entry.Fields.Select(f => $"{f.Name}={f.Value}"));
    }

    public string Id { get; }

    public string Body { get; }

    public string DisplayBody => Body.Length <= 100 ? Body : Body[..100] + "...";

    public bool CanWrite => _owner.CanWrite;

    public UiStrings Loc => _owner.Loc;

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteEntryCommand.ExecuteAsync(this);
}

public partial class PubSubTabViewModel : ViewModelBase, IWorkspaceTab
{
    private readonly MainWindowViewModel _owner;

    public PubSubTabViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection)
    {
        _owner = owner;
        Connection = connection;
        Title = $"{connection.Config.Name} Pub/Sub";
    }

    public UiStrings Loc => _owner.Loc;

    public string Title { get; }

    public ConnectionItemViewModel Connection { get; }

    public override string ToString() => Title;

    public ObservableCollection<string> Messages { get; } = [];

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private bool _usePattern;

    [ObservableProperty]
    private string _publishChannel = string.Empty;

    [ObservableProperty]
    private string _publishMessage = string.Empty;

    [ObservableProperty]
    private string _statusText = "订阅频道后接收消息。断开连接会自动退订。";

    public bool CanWrite => !Connection.Config.ReadOnly;

    public bool IsSubscribed => Connection.Session?.PubSub.IsSubscribed == true;

    [RelayCommand]
    private async Task SubscribeAsync()
    {
        if (Connection.Session is null || string.IsNullOrWhiteSpace(Channel))
        {
            return;
        }

        try
        {
            Connection.Session.PubSub.MessageReceived -= OnMessage;
            Connection.Session.PubSub.MessageReceived += OnMessage;
            await Connection.Session.PubSub.SubscribeAsync(Channel.Trim(), UsePattern);
            StatusText = UsePattern ? $"已 PSUBSCRIBE {Channel}" : $"已 SUBSCRIBE {Channel}";
            OnPropertyChanged(nameof(IsSubscribed));
        }
        catch (Exception ex)
        {
            await _owner.Prompt.ErrorAsync("订阅失败", ex);
        }
    }

    [RelayCommand]
    private async Task UnsubscribeAsync()
    {
        if (Connection.Session is null)
        {
            return;
        }

        try
        {
            Connection.Session.PubSub.MessageReceived -= OnMessage;
            await Connection.Session.PubSub.UnsubscribeAsync();
            StatusText = "已退订";
            OnPropertyChanged(nameof(IsSubscribed));
        }
        catch (Exception ex)
        {
            await _owner.Prompt.ErrorAsync("退订失败", ex);
        }
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (Connection.Session is null || string.IsNullOrWhiteSpace(PublishChannel))
        {
            return;
        }

        try
        {
            await Connection.Session.PubSub.PublishAsync(PublishChannel.Trim(), PublishMessage);
            _owner.Prompt.Info("已发布");
        }
        catch (Exception ex)
        {
            await _owner.Prompt.ErrorAsync("PUBLISH 失败", ex);
        }
    }

    public async Task StopAsync()
    {
        if (Connection.Session is null)
        {
            return;
        }

        Connection.Session.PubSub.MessageReceived -= OnMessage;
        try
        {
            await Connection.Session.PubSub.UnsubscribeAsync();
        }
        catch
        {
            // closing
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await StopAsync();
        await _owner.Workspace.CloseTabCommand.ExecuteAsync(this);
    }

    private void OnMessage(object? sender, PubSubMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Messages.Insert(0, $"{message.Timestamp:HH:mm:ss}  {message.Channel}  {message.Payload}");
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        });
    }
}
