using System.Collections.ObjectModel;
using AtomUI.Controls;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class KeyBrowserViewModel : ViewModelBase
{
    public const int OverflowLimit = 200_000;

    private readonly ConnectionWorkspaceViewModel _workspace;
    private readonly List<RedisKeyBytes> _keys = [];
    private readonly HashSet<RedisKeyBytes> _seen = [];
    private CancellationTokenSource? _cts;
    private long _cursor;
    private bool _exhausted;

    public KeyBrowserViewModel(ConnectionWorkspaceViewModel workspace)
    {
        _workspace = workspace;
    }

    public UiStrings Loc => _workspace.Owner.Loc;

    public bool CanWrite => !_workspace.IsReadOnly;

    [ObservableProperty]
    private string _pattern = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _isExactSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTreeMode))]
    [NotifyPropertyChangedFor(nameof(IsFlatMode))]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    [NotifyPropertyChangedFor(nameof(CanLoadAll))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _hasMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyMenu))]
    private KeyListItemViewModel? _selectedFlatItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyMenu))]
    [NotifyPropertyChangedFor(nameof(ShowFolderMenu))]
    private KeyNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBatchBar))]
    [NotifyPropertyChangedFor(nameof(ShowKeyMenu))]
    [NotifyPropertyChangedFor(nameof(ShowFolderMenu))]
    private bool _isMultiSelect;

    [ObservableProperty]
    private bool _isCheckAll;

    public bool OpenInNewTabOnce { get; set; }

    public bool SuppressOpenOnce { get; set; }

    public bool IsTreeMode => UseTree;

    public bool IsFlatMode => !UseTree;

    public bool ShowEmpty => !IsBusy && _keys.Count == 0;

    public bool ShowBatchBar => IsMultiSelect;

    public bool CanLoadMore => HasMore && !IsBusy && !IsExactSearch;

    public bool CanLoadAll => !IsBusy;

    public bool ShowKeyMenu =>
        !IsMultiSelect && (SelectedTreeNode is { IsKey: true } || SelectedFlatItem is not null);

    public bool ShowFolderMenu => !IsMultiSelect && SelectedTreeNode is { IsFolder: true };

    public ObservableCollection<KeyListItemViewModel> FlatKeys { get; } = [];

    public ObservableCollection<KeyNodeViewModel> TreeRoots { get; } = [];

    private bool UseTree => !string.IsNullOrEmpty(_workspace.Connection.Config.KeySeparator);

    private string Separator => _workspace.Connection.Config.KeySeparator ?? string.Empty;

    public void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
        IsBusy = false;
    }

    public void ResetSearchCriteria()
    {
        Pattern = string.Empty;
        IsExactSearch = false;
        ExitMultiSelect();
    }

    public void AddScannedKey(RedisKeyBytes key)
    {
        if (!_seen.Add(key))
        {
            return;
        }

        _keys.Add(key);
        RebuildView();
    }

    public void RemoveScannedKey(RedisKeyBytes key)
    {
        if (!_seen.Remove(key))
        {
            return;
        }

        _keys.RemoveAll(item => item.Equals(key));
        RebuildView();
    }

    [RelayCommand]
    private void CancelSearch() => Cancel();

    [RelayCommand]
    public async Task SearchAsync()
    {
        await ScanCoreAsync(reset: true, loadAll: false);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore)
        {
            return;
        }

        await ScanCoreAsync(reset: false, loadAll: false);
    }

    [RelayCommand]
    private async Task LoadAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ScanCoreAsync(reset: true, loadAll: true);
    }

    [RelayCommand]
    private void BeginMultiSelect()
    {
        IsMultiSelect = true;
        IsCheckAll = false;
        SetAllChecked(false);
    }

    [RelayCommand]
    private void ExitMultiSelect()
    {
        IsMultiSelect = false;
        IsCheckAll = false;
        SetAllChecked(false);
    }

    partial void OnIsCheckAllChanged(bool value)
    {
        if (IsMultiSelect)
        {
            SetAllChecked(value);
        }
    }

    [RelayCommand]
    private async Task CopyKeyAsync()
    {
        var name = SelectedTreeNode is { IsKey: true } node
            ? node.Title
            : SelectedFlatItem?.Display;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var top = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (top?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(name);
            _workspace.Owner.Prompt.Info(Loc.T("已复制", "Copied"));
        }
    }

    [RelayCommand]
    private async Task DeleteKeyAsync()
    {
        if (!CanWrite)
        {
            return;
        }

        if (IsMultiSelect)
        {
            await DeleteCheckedAsync();
            return;
        }

        var key = SelectedTreeNode?.FullKey ?? SelectedFlatItem?.Key;
        if (key is not { } target)
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync(Loc.DeleteKey, Loc.T($"确定删除「{target.ToDisplayString()}」？", $"Delete '{target.ToDisplayString()}'?")))
        {
            return;
        }

        try
        {
            await _workspace.Session.Values.DeleteAsync(target);
            RemoveScannedKey(target);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("删除失败", "Delete failed"), ex);
        }
    }

    [RelayCommand]
    private Task OpenNewTabAsync()
    {
        var key = SelectedTreeNode?.FullKey ?? SelectedFlatItem?.Key;
        return key is { } target
            ? _workspace.Owner.Workspace.OpenKeyAsync(_workspace.Connection, target, newTab: true)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task LoadCurrentFolderAsync()
    {
        if (SelectedTreeNode is not { IsFolder: true } folder)
        {
            return Task.CompletedTask;
        }

        Pattern = folder.FolderPath;
        IsExactSearch = false;
        return SearchAsync();
    }

    [RelayCommand]
    private Task AnalyzeFolderAsync()
    {
        if (SelectedTreeNode is not { IsFolder: true } folder)
        {
            return Task.CompletedTask;
        }

        return _workspace.Owner.OpenMemoryAnalysisAsync(_workspace.Connection, folder.FolderPath);
    }

    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (!CanWrite || SelectedTreeNode is not { IsFolder: true } folder)
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync(
                Loc.DeleteFolder,
                Loc.T($"将扫描并删除文件夹「{folder.Title}」下的 Key，确定？", $"Scan and delete keys under '{folder.Title}'. Continue?")))
        {
            return;
        }

        await DeleteByMatchAsync(folder.FolderPath + "*");
    }

    [RelayCommand]
    private async Task DeleteCheckedAsync()
    {
        if (!CanWrite)
        {
            return;
        }

        var keys = CollectCheckedKeys();
        if (keys.Count == 0)
        {
            _workspace.Owner.Prompt.Info(Loc.T("请先勾选 Key", "Please select keys"));
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync(
                Loc.DeleteKey,
                Loc.T($"确定删除选中的 {keys.Count} 个 Key？", $"Delete {keys.Count} selected keys?")))
        {
            return;
        }

        try
        {
            foreach (var key in keys)
            {
                await _workspace.Session.Values.DeleteAsync(key);
                _seen.Remove(key);
                _keys.RemoveAll(item => item.Equals(key));
            }

            RebuildView();
            ExitMultiSelect();
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("删除失败", "Delete failed"), ex);
        }
    }

    public Task DetailOpen(RedisKeyBytes key, bool newTab = false)
        => _workspace.Owner.Workspace.OpenKeyAsync(_workspace.Connection, key, newTab);

    partial void OnSelectedFlatItemChanged(KeyListItemViewModel? value)
    {
        if (value is null || IsMultiSelect || SuppressOpenOnce)
        {
            OpenInNewTabOnce = false;
            SuppressOpenOnce = false;
            return;
        }

        var newTab = OpenInNewTabOnce;
        OpenInNewTabOnce = false;
        _ = DetailOpen(value.Key, newTab);
    }

    partial void OnSelectedTreeNodeChanged(KeyNodeViewModel? value)
    {
        if (value is not { IsKey: true, FullKey: { } key } || IsMultiSelect || SuppressOpenOnce)
        {
            OpenInNewTabOnce = false;
            SuppressOpenOnce = false;
            return;
        }

        var newTab = OpenInNewTabOnce;
        OpenInNewTabOnce = false;
        _ = DetailOpen(key, newTab);
    }

    private async Task ScanCoreAsync(bool reset, bool loadAll)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        if (reset)
        {
            _keys.Clear();
            _seen.Clear();
            _cursor = 0;
            _exhausted = false;
            HasMore = false;
            RebuildView();
        }

        try
        {
            var match = ResolveMatch();
            if (reset && IsExactSearch && !string.IsNullOrWhiteSpace(Pattern))
            {
                var key = RedisKeyBytes.FromUtf8(Pattern.Trim());
                if (await _workspace.Session.Keys.ExistsAsync(key, token))
                {
                    _seen.Add(key);
                    _keys.Add(key);
                }

                _exhausted = true;
                HasMore = false;
                RebuildView();
                return;
            }

            var count = loadAll
                ? 50_000
                : match is "*"
                    ? Math.Max(1, _workspace.Owner.ScanCount)
                    : Math.Max(_workspace.Owner.ScanCount, 10_000);

            do
            {
                var page = await _workspace.Session.Keys.ScanAsync(
                    new ScanRequest(_workspace.Session.CurrentDatabase, match, _cursor, count),
                    token);
                foreach (var key in page.Keys)
                {
                    if (_keys.Count >= OverflowLimit)
                    {
                        break;
                    }

                    if (_seen.Add(key))
                    {
                        _keys.Add(key);
                    }
                }

                _cursor = page.Cursor;
                _exhausted = page.Exhausted || _keys.Count >= OverflowLimit;
                HasMore = !_exhausted;
                if (!loadAll)
                {
                    break;
                }
            }
            while (!_exhausted && !token.IsCancellationRequested);

            RebuildView();
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("已取消", "Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = IsScanDisabled(ex)
                ? Loc.ScanDisabled
                : Loc.T("扫描失败", "Scan failed");
            await _workspace.Owner.Prompt.ErrorAsync(StatusText, ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    private void RebuildView()
    {
        FlatKeys.Clear();
        TreeRoots.Clear();
        if (UseTree)
        {
            var truncated = _keys.Count >= OverflowLimit;
            var nodes = KeyTreeBuilder.BuildTree(_keys, Separator, OverflowLimit);
            var expandAll = _keys.Count > 0 && _keys.Count <= 20;
            foreach (var node in nodes)
            {
                var root = new KeyNodeViewModel(this, node);
                if (expandAll)
                {
                    root.ExpandAll();
                }

                TreeRoots.Add(root);
            }

            StatusText = truncated
                ? string.Format(Loc.TreeNodeOverflow, OverflowLimit)
                : Loc.T($"共 {_keys.Count} 个 Key", $"{_keys.Count} keys");
        }
        else
        {
            foreach (var key in _keys)
            {
                FlatKeys.Add(new KeyListItemViewModel(this, key));
            }

            StatusText = _keys.Count >= OverflowLimit
                ? string.Format(Loc.TreeNodeOverflow, OverflowLimit)
                : Loc.T($"共 {_keys.Count} 个 Key", $"{_keys.Count} keys");
        }

        OnPropertyChanged(nameof(IsTreeMode));
        OnPropertyChanged(nameof(IsFlatMode));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private string ResolveMatch()
    {
        var raw = Pattern.Trim();
        if (IsExactSearch)
        {
            return string.IsNullOrEmpty(raw) ? "*" : raw;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return "*";
        }

        return raw.Contains('*') ? raw : $"*{raw}*";
    }

    private async Task DeleteByMatchAsync(string match)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        try
        {
            long cursor = 0;
            var deleted = 0;
            do
            {
                var page = await _workspace.Session.Keys.ScanAsync(
                    new ScanRequest(_workspace.Session.CurrentDatabase, match, cursor, 1000),
                    token);
                foreach (var key in page.Keys)
                {
                    await _workspace.Session.Values.DeleteAsync(key, token);
                    _seen.Remove(key);
                    _keys.RemoveAll(item => item.Equals(key));
                    deleted++;
                }

                cursor = page.Cursor;
            }
            while (cursor != 0 && deleted < OverflowLimit && !token.IsCancellationRequested);

            RebuildView();
            _workspace.Owner.Prompt.Info(Loc.T($"已删除 {deleted} 个 Key", $"Deleted {deleted} keys"));
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("删除失败", "Delete failed"), ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<RedisKeyBytes> CollectCheckedKeys()
    {
        var keys = new List<RedisKeyBytes>();
        if (UseTree)
        {
            foreach (var root in TreeRoots)
            {
                root.CollectChecked(keys);
            }
        }
        else
        {
            foreach (var item in FlatKeys)
            {
                if (item.IsChecked)
                {
                    keys.Add(item.Key);
                }
            }
        }

        return keys;
    }

    private void SetAllChecked(bool value)
    {
        foreach (var root in TreeRoots)
        {
            root.SetCheckedRecursive(value);
        }

        foreach (var item in FlatKeys)
        {
            item.IsChecked = value;
        }
    }

    private static bool IsScanDisabled(Exception ex)
    {
        var message = ex.Message;
        return (message.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
                && message.Contains("scan", StringComparison.OrdinalIgnoreCase))
               || message.Contains("command 'SCAN' is not allowed", StringComparison.OrdinalIgnoreCase);
    }
}

public partial class KeyListItemViewModel : ViewModelBase
{
    public KeyListItemViewModel(KeyBrowserViewModel browser, RedisKeyBytes key)
    {
        Browser = browser;
        Key = key;
        Display = key.ToDisplayString();
    }

    public KeyBrowserViewModel Browser { get; }

    public RedisKeyBytes Key { get; }

    public string Display { get; }

    [ObservableProperty]
    private bool _isChecked;
}

public partial class KeyNodeViewModel : ViewModelBase, ITreeItemNode
{
    private ITreeItemNode? _parentNode;

    public KeyNodeViewModel(KeyBrowserViewModel browser, KeyTreeNode node)
    {
        Browser = browser;
        Title = node.Name;
        IsFolder = node.Kind == KeyTreeNodeKind.Folder;
        FullKey = node.FullKey;
        FolderPath = node.Path;
        ChildCount = node.ChildCount;
        ItemKey = new EntityKey(IsFolder ? $"folder:{FolderPath}" : $"key:{FullKey?.ToDisplayString() ?? Title}");
        Value = this;
        foreach (var child in node.Children)
        {
            var childNode = new KeyNodeViewModel(browser, child);
            childNode.UpdateParentNode(this);
            Children.Add(childNode);
        }
    }

    public KeyBrowserViewModel Browser { get; }

    public string Title { get; }

    public int ChildCount { get; }

    public string CountText => IsFolder ? $"({ChildCount})" : string.Empty;

    public bool IsFolder { get; }

    public bool IsKey => !IsFolder;

    public RedisKeyBytes? FullKey { get; }

    public string FolderPath { get; }

    public bool ShowClosedFolder => IsFolder && !IsExpanded;

    public bool ShowOpenFolder => IsFolder && IsExpanded;

    public ObservableCollection<KeyNodeViewModel> Children { get; } = [];

    public ITreeNode<ITreeItemNode>? ParentNode => _parentNode;

    public object? Header => Title;

    public Avalonia.Controls.PathIcon? Icon => null;

    public bool IsEnabled { get; set; } = true;

    IEnumerable<ITreeItemNode> ITreeNode<ITreeItemNode>.Children => Children;

    public EntityKey? ItemKey { get; }

    bool? ITreeItemNode.IsChecked
    {
        get => IsChecked;
        set => IsChecked = value == true;
    }

    public bool IsSelected { get; set; }

    public bool IsIndicatorEnabled { get; set; } = true;

    public string? GroupName => null;

    public bool IsLeaf => IsKey;

    public object? Value { get; set; }

    public void UpdateParentNode(ITreeItemNode? parentNode) => _parentNode = parentNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowClosedFolder))]
    [NotifyPropertyChangedFor(nameof(ShowOpenFolder))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (IsFolder)
        {
            foreach (var child in Children)
            {
                child.IsChecked = value;
            }
        }
    }

    public void ExpandAll()
    {
        if (!IsFolder)
        {
            return;
        }

        IsExpanded = true;
        foreach (var child in Children)
        {
            child.ExpandAll();
        }
    }

    public void SetCheckedRecursive(bool value)
    {
        IsChecked = value;
        foreach (var child in Children)
        {
            child.SetCheckedRecursive(value);
        }
    }

    public void CollectChecked(List<RedisKeyBytes> keys)
    {
        if (IsKey && IsChecked && FullKey is { } key)
        {
            keys.Add(key);
        }

        foreach (var child in Children)
        {
            child.CollectChecked(keys);
        }
    }
}
