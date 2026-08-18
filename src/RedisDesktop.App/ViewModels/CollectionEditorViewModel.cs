using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class CollectionEditorViewModel : ViewModelBase
{
    private readonly ConnectionWorkspaceViewModel _workspace;
    private RedisKeyBytes _key = RedisKeyBytes.FromUtf8(string.Empty);
    private long _pageCursor;
    private long _nextCursor;
    private readonly Stack<long> _cursorHistory = new();

    public CollectionEditorViewModel(ConnectionWorkspaceViewModel workspace)
    {
        _workspace = workspace;
    }

    public UiStrings Loc => _workspace.Owner.Loc;

    public ObservableCollection<CollectionRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private RedisKeyType _keyType = RedisKeyType.Hash;

    [ObservableProperty]
    private int _pageSize = 50;

    [ObservableProperty]
    private string _match = "*";

    [ObservableProperty]
    private string _clientFilter = string.Empty;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newValue = string.Empty;

    [ObservableProperty]
    private string _newScore = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdHeader))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canGoPrev;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTtlColumn))]
    private bool _supportsFieldTtl;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditTitle))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditTitle))]
    private bool _isAdding;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editValue = string.Empty;

    [ObservableProperty]
    private string _editScore = "0";

    [ObservableProperty]
    private string _editTtl = "-1";

    private CollectionRowViewModel? _editingRow;

    public ObservableCollection<CollectionRowViewModel> FilteredRows { get; } = [];

    public bool IsHash => KeyType == RedisKeyType.Hash;

    public bool IsList => KeyType == RedisKeyType.List;

    public bool IsSet => KeyType == RedisKeyType.Set;

    public bool IsZSet => KeyType == RedisKeyType.SortedSet;

    public bool ShowNameColumn => IsHash || IsSet || IsZSet;

    public bool ShowValueColumn => IsHash || IsList;

    public bool ShowScoreColumn => IsZSet;

    public bool ShowIndexColumn => true;

    public bool ShowTtlColumn => SupportsFieldTtl && IsHash;

    public string IdHeader => string.IsNullOrEmpty(StatusText) ? "ID" : $"ID ({StatusText})";

    public string EditTitle => IsAdding ? Loc.AddNewLine : Loc.EditLine;

    public bool CanWrite => !_workspace.IsReadOnly;

    public bool IsDirty => IsEditing || Rows.Any(r => r.IsDirty);

    public string ListHint => IsList ? "按索引修改基于当前快照" : string.Empty;

    public async Task LoadAsync(RedisKeyBytes key, RedisKeyType type)
    {
        _key = key;
        KeyType = type;
        _pageCursor = 0;
        _cursorHistory.Clear();
        CanGoPrev = false;
        RefreshTypeFlags();
        await LoadPageAsync(reset: true);
    }

    public void Clear()
    {
        Rows.Clear();
        FilteredRows.Clear();
        StatusText = string.Empty;
        SupportsFieldTtl = false;
        _cursorHistory.Clear();
        CanGoPrev = false;
        CanGoNext = false;
        IsEditing = false;
        _editingRow = null;
    }

    [RelayCommand]
    private Task RefreshPageAsync() => LoadPageAsync(reset: true);

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext || IsBusy)
        {
            return;
        }

        _cursorHistory.Push(_pageCursor);
        _pageCursor = _nextCursor;
        await LoadPageAsync(reset: false);
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanGoPrev || IsBusy)
        {
            return;
        }

        _pageCursor = _cursorHistory.Count > 0 ? _cursorHistory.Pop() : 0;
        await LoadPageAsync(reset: false);
        CanGoPrev = _cursorHistory.Count > 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite)
        {
            return;
        }

        try
        {
            var values = _workspace.Session.Values;
            switch (KeyType)
            {
                case RedisKeyType.Hash:
                    if (string.IsNullOrEmpty(NewName))
                    {
                        return;
                    }

                    await values.HashSetAsync(_key, NewName, NewValue, null);
                    break;
                case RedisKeyType.List:
                    await values.ListPushAsync(_key, NewValue, left: false);
                    break;
                case RedisKeyType.Set:
                    if (string.IsNullOrEmpty(NewName))
                    {
                        return;
                    }

                    await values.SetAddAsync(_key, NewName);
                    break;
                case RedisKeyType.SortedSet:
                    if (string.IsNullOrEmpty(NewName) || !double.TryParse(NewScore, out var score))
                    {
                        return;
                    }

                    await values.ZAddAsync(_key, NewName, score);
                    break;
            }

            NewName = string.Empty;
            NewValue = string.Empty;
            await LoadPageAsync(reset: true);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("新增失败", ex);
        }
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!CanWrite)
        {
            return;
        }

        IsAdding = true;
        _editingRow = null;
        EditName = string.Empty;
        EditValue = string.Empty;
        EditScore = "0";
        EditTtl = "-1";
        IsEditing = true;
    }

    [RelayCommand]
    private void BeginEdit(CollectionRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        IsAdding = false;
        _editingRow = row;
        EditName = row.Name;
        EditValue = row.Value;
        EditScore = row.ScoreText;
        EditTtl = string.IsNullOrEmpty(row.TtlInput) ? "-1" : row.TtlInput;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        _editingRow = null;
    }

    [RelayCommand]
    private async Task CommitEditAsync()
    {
        if (IsAdding)
        {
            NewName = EditName;
            NewValue = EditValue;
            NewScore = EditScore;
            await AddAsync();
            if (string.IsNullOrEmpty(NewName) && string.IsNullOrEmpty(NewValue))
            {
                IsEditing = false;
                _editingRow = null;
            }

            return;
        }

        if (_editingRow is not { } row)
        {
            return;
        }

        row.Name = EditName;
        row.Value = EditValue;
        row.ScoreText = EditScore;
        row.TtlInput = EditTtl;
        await SaveRowAsync(row);
        IsEditing = false;
        _editingRow = null;
    }

    [RelayCommand]
    private Task CopyRowAsync(CollectionRowViewModel? row)
    {
        var text = row is null ? null : ShowValueColumn ? row.Value : row.Name;
        return CopyTextAsync(text);
    }

    [RelayCommand]
    private Task DumpRowAsync(CollectionRowViewModel? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var key = ValueViewerPipeline.QuoteRedisArg(_key.Value);
        var name = ValueViewerPipeline.QuoteRedisArg(System.Text.Encoding.UTF8.GetBytes(row.Name));
        var value = ValueViewerPipeline.QuoteRedisArg(System.Text.Encoding.UTF8.GetBytes(row.Value));
        var command = KeyType switch
        {
            RedisKeyType.Hash => $"HSET {key} {name} {value}",
            RedisKeyType.List => $"LSET {key} {row.Index ?? 0} {value}",
            RedisKeyType.Set => $"SADD {key} {name}",
            RedisKeyType.SortedSet => $"ZADD {key} {row.ScoreText} {name}",
            _ => string.Empty
        };
        return CopyTextAsync(command);
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
            _workspace.Owner.Prompt.Info(Loc.Copied);
        }
    }

    [RelayCommand]
    private async Task SaveRowAsync(CollectionRowViewModel? row)
    {
        if (row is null || !CanWrite)
        {
            return;
        }

        try
        {
            var values = _workspace.Session.Values;
            switch (KeyType)
            {
                case RedisKeyType.Hash:
                    if (row.OriginalName != row.Name && !string.IsNullOrEmpty(row.OriginalName))
                    {
                        await values.HashDeleteAsync(_key, row.OriginalName);
                    }

                    TimeSpan? ttl = int.TryParse(row.TtlInput, out var seconds) && seconds > 0
                        ? TimeSpan.FromSeconds(seconds)
                        : null;
                    await values.HashSetAsync(_key, row.Name, row.Value, ttl);
                    break;
                case RedisKeyType.List:
                    if (row.Index is { } index)
                    {
                        await values.ListSetAsync(_key, index, row.Value);
                    }

                    break;
                case RedisKeyType.Set:
                    if (!string.IsNullOrEmpty(row.OriginalName) && row.OriginalName != row.Name)
                    {
                        await values.SetRemoveAsync(_key, row.OriginalName);
                    }

                    await values.SetAddAsync(_key, row.Name);
                    break;
                case RedisKeyType.SortedSet:
                    if (!double.TryParse(row.ScoreText, out var score))
                    {
                        await _workspace.Owner.Prompt.ErrorAsync("Score 无效");
                        return;
                    }

                    if (!string.IsNullOrEmpty(row.OriginalName) && row.OriginalName != row.Name)
                    {
                        await values.ZRemAsync(_key, row.OriginalName);
                    }

                    await values.ZAddAsync(_key, row.Name, score);
                    break;
            }

            row.AcceptChanges();
            _workspace.Owner.Prompt.Info("已保存");
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("保存行失败", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteRowAsync(CollectionRowViewModel? row)
    {
        if (row is null || !CanWrite)
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync("删除成员", $"确定删除「{row.Name}」？"))
        {
            return;
        }

        try
        {
            var values = _workspace.Session.Values;
            switch (KeyType)
            {
                case RedisKeyType.Hash:
                    await values.HashDeleteAsync(_key, row.OriginalName);
                    break;
                case RedisKeyType.List:
                    if (row.Index is { } index)
                    {
                        await values.ListRemoveAtAsync(_key, index);
                    }

                    break;
                case RedisKeyType.Set:
                    await values.SetRemoveAsync(_key, row.OriginalName);
                    break;
                case RedisKeyType.SortedSet:
                    await values.ZRemAsync(_key, row.OriginalName);
                    break;
            }

            await LoadPageAsync(reset: true);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("删除失败", ex);
        }
    }

    private async Task LoadPageAsync(bool reset)
    {
        IsBusy = true;
        try
        {
            if (reset)
            {
                _pageCursor = 0;
                _nextCursor = 0;
                _cursorHistory.Clear();
                CanGoPrev = false;
            }

            var values = _workspace.Session.Values;
            var size = Math.Clamp(PageSize, 10, 500);
            var page = KeyType switch
            {
                RedisKeyType.Hash => await values.ScanHashAsync(_key, _pageCursor, size, Match),
                RedisKeyType.List => await values.RangeListAsync(_key, _pageCursor, _pageCursor + size - 1),
                RedisKeyType.Set => await values.ScanSetAsync(_key, _pageCursor, size, Match),
                RedisKeyType.SortedSet => await values.ScanZSetAsync(_key, _pageCursor, size, Match),
                _ => new CollectionPage([], 0, true, 0, 0)
            };

            Rows.Clear();
            var n = 0;
            foreach (var member in page.Items)
            {
                n++;
                Rows.Add(new CollectionRowViewModel(member, page.SupportsFieldTtl, this, member.Index ?? n));
            }

            SupportsFieldTtl = page.SupportsFieldTtl && IsHash;
            CanGoNext = !page.Exhausted;
            _nextCursor = page.Cursor;
            CanGoPrev = _cursorHistory.Count > 0;
            var total = page.TotalCount is { } t ? $" / 共 {t}" : string.Empty;
            StatusText = $"已加载 {page.LoadedCount}{total}";
            RebuildFilter();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IdHeader));
            OnPropertyChanged(nameof(ShowTtlColumn));
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync("加载集合失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildFilter()
    {
        FilteredRows.Clear();
        var filter = ClientFilter?.Trim() ?? string.Empty;
        foreach (var row in Rows)
        {
            if (string.IsNullOrEmpty(filter)
                || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRows.Add(row);
            }
        }
    }

    private void RefreshTypeFlags()
    {
        OnPropertyChanged(nameof(IsHash));
        OnPropertyChanged(nameof(IsList));
        OnPropertyChanged(nameof(IsSet));
        OnPropertyChanged(nameof(IsZSet));
        OnPropertyChanged(nameof(ShowNameColumn));
        OnPropertyChanged(nameof(ShowValueColumn));
        OnPropertyChanged(nameof(ShowScoreColumn));
        OnPropertyChanged(nameof(ShowIndexColumn));
        OnPropertyChanged(nameof(ShowTtlColumn));
        OnPropertyChanged(nameof(ListHint));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(IdHeader));
    }

    partial void OnClientFilterChanged(string value) => RebuildFilter();
}

public partial class CollectionRowViewModel : ViewModelBase
{
    public CollectionRowViewModel(CollectionMember member, bool supportsTtl, CollectionEditorViewModel owner, long displayIndex)
    {
        _owner = owner;
        OriginalName = member.Name;
        OriginalValue = member.Value;
        OriginalScore = member.Score;
        Index = member.Index;
        Name = member.Name;
        Value = member.Value;
        ScoreText = member.Score?.ToString("G") ?? string.Empty;
        TtlInput = member.FieldTtl is { } ttl ? ((int)ttl.TotalSeconds).ToString() : string.Empty;
        ShowTtl = supportsTtl;
        IndexText = displayIndex.ToString();
    }

    private readonly CollectionEditorViewModel _owner;

    public UiStrings Loc => _owner.Loc;

    public string OriginalName { get; private set; }

    public string OriginalValue { get; private set; }

    public double? OriginalScore { get; private set; }

    public long? Index { get; }

    public string IndexText { get; }

    public string DisplayName => Cut(Name, 80);

    public string DisplayValue => Cut(Value, 100);

    public bool ShowTtl { get; }

    public bool CanWrite => _owner.CanWrite;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _scoreText = string.Empty;

    [ObservableProperty]
    private string _ttlInput = string.Empty;

    public bool IsDirty =>
        Name != OriginalName
        || Value != OriginalValue
        || ScoreText != (OriginalScore?.ToString("G") ?? string.Empty);

    public void AcceptChanges()
    {
        OriginalName = Name;
        OriginalValue = Value;
        if (double.TryParse(ScoreText, out var score))
        {
            OriginalScore = score;
        }

        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand]
    private Task SaveAsync() => _owner.SaveRowCommand.ExecuteAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteRowCommand.ExecuteAsync(this);

    [RelayCommand]
    private Task EditAsync()
    {
        _owner.BeginEditCommand.Execute(this);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CopyAsync() => _owner.CopyRowCommand.ExecuteAsync(this);

    [RelayCommand]
    private Task DumpAsync() => _owner.DumpRowCommand.ExecuteAsync(this);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayValue));
    }

    partial void OnScoreTextChanged(string value) => OnPropertyChanged(nameof(IsDirty));

    private static string Cut(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + "...";
}
