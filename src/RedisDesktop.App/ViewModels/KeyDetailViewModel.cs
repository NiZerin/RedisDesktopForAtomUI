using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public sealed record ViewerOption(ValueViewKind Kind, string Display)
{
    public override string ToString() => Display;
}

public partial class KeyDetailViewModel : ViewModelBase
{
    public const long MaxFullLoad = 1_000_000;
    public const long PreviewLimit = 64_000;

    private readonly ConnectionWorkspaceViewModel _workspace;
    private readonly DispatcherTimer _ttlTimer;
    private readonly DispatcherTimer _autoRefreshTimer;
    private RedisKeyBytes? _currentKey;
    private byte[] _original = [];
    private string _originalText = string.Empty;
    private DateTimeOffset? _ttlEnd;
    private bool _suppressViewer;
    private bool _canEncode = true;

    public KeyDetailViewModel(ConnectionWorkspaceViewModel workspace)
    {
        _workspace = workspace;
        Collection = new CollectionEditorViewModel(workspace);
        Stream = new StreamEditorViewModel(workspace);
        ViewerOptions = ValueViewerPipeline.ManualKinds
            .Select(kind => new ViewerOption(kind, ValueViewerPipeline.DisplayName(kind)))
            .ToArray();
        SelectedViewerOption = ViewerOptions[0];
        _ttlTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ttlTimer.Tick += (_, _) => TickTtl();
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoRefreshTimer.Tick += (_, _) => _ = AutoRefreshTickAsync();
    }

    public UiStrings Loc => _workspace.Owner.Loc;

    public CollectionEditorViewModel Collection { get; }

    public StreamEditorViewModel Stream { get; }

    public IReadOnlyList<ViewerOption> ViewerOptions { get; }

    [ObservableProperty]
    private bool _hasKey;

    [ObservableProperty]
    private string _keyName = string.Empty;

    [ObservableProperty]
    private string _typeName = string.Empty;

    [ObservableProperty]
    private string _ttlText = string.Empty;

    [ObservableProperty]
    private string _ttlInput = "-1";

    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveString))]
    private bool _isString;

    [ObservableProperty]
    private bool _isCollection;

    [ObservableProperty]
    private bool _isStream;

    [ObservableProperty]
    private ValueViewKind _selectedViewer = ValueViewKind.Text;

    [ObservableProperty]
    private ViewerOption? _selectedViewerOption;

    [ObservableProperty]
    private string _viewerWarning = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeViewer))]
    [NotifyPropertyChangedFor(nameof(CanSaveString))]
    private bool _isTruncated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeViewer))]
    private bool _isBusy;

    [ObservableProperty]
    private string _unsupportedMessage = string.Empty;

    [ObservableProperty]
    private string _renameTo = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private bool _showHexTag;

    [ObservableProperty]
    private bool _autoRefresh;

    public bool CanWrite => !_workspace.IsReadOnly && HasKey;

    public bool CanSaveString => CanWrite && IsString && _canEncode && !IsTruncated;

    public bool CanChangeViewer => IsString && !IsTruncated && !IsBusy;

    public bool ShowUnsupported => !string.IsNullOrEmpty(UnsupportedMessage);

    public bool ShowViewerWarning => !string.IsNullOrEmpty(ViewerWarning);

    public bool IsDirty =>
        (IsString && HasKey && ValueText != _originalText)
        || (IsCollection && Collection.IsDirty);

    [RelayCommand]
    public async Task OpenAsync(RedisKeyBytes key)
    {
        await OpenCoreAsync(key, confirmDirty: true);
    }

    private async Task OpenCoreAsync(RedisKeyBytes key, bool confirmDirty)
    {
        if (confirmDirty && IsDirty && !await _workspace.Owner.Prompt.ConfirmAsync(Loc.Unsaved, Loc.UnsavedSwitchKey))
        {
            return;
        }

        _currentKey = key;
        HasKey = true;
        KeyName = key.ToDisplayString();
        RenameTo = KeyName;
        IsBusy = true;
        try
        {
            var meta = await _workspace.Session.Values.GetMetaAsync(key);
            TypeName = ValueViewerPipeline.TypeLabel(meta.Type);
            TtlInput = meta.TimeToLive is { } ttl && ttl > TimeSpan.Zero
                ? ((int)Math.Max(0, ttl.TotalSeconds)).ToString()
                : "-1";
            ApplyTtlCountdown(meta.TimeToLive);
            IsString = meta.Type == RedisKeyType.String;
            IsCollection = meta.Type is RedisKeyType.Hash or RedisKeyType.List or RedisKeyType.Set or RedisKeyType.SortedSet;
            IsStream = meta.Type == RedisKeyType.Stream;
            UnsupportedMessage = IsString || IsCollection || IsStream
                ? string.Empty
                : Loc.T($"暂不支持 {TypeName} 类型的查看与编辑。", $"{TypeName} is not supported yet.");

            Collection.Clear();
            Stream.Clear();
            ValueText = string.Empty;
            _original = [];
            _originalText = string.Empty;
            IsTruncated = false;
            ViewerWarning = string.Empty;
            SizeText = string.Empty;
            ShowHexTag = false;

            if (IsString)
            {
                var snapshot = await _workspace.Session.Values.GetStringAsync(key, PreviewLimit, MaxFullLoad);
                _original = snapshot.Value;
                IsTruncated = snapshot.IsTruncated;
                SizeText = $"Size: {ValueViewerPipeline.FormatByteSize(snapshot.TotalLength)}";
                ShowHexTag = !snapshot.IsUtf8;
                var kind = IsTruncated
                    ? ValueViewKind.OverSize
                    : ValueViewerPipeline.Detect(_original);
                ApplyViewer(kind);
            }
            else if (IsCollection)
            {
                await Collection.LoadAsync(key, meta.Type);
            }
            else if (IsStream)
            {
                await Stream.LoadAsync(key);
            }
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("读取 Key 失败", "Failed to read key"), ex);
        }
        finally
        {
            IsBusy = false;
            NotifyEditorState();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_currentKey is not { } key)
        {
            return;
        }

        if (IsDirty && !await _workspace.Owner.Prompt.ConfirmAsync(Loc.Unsaved, Loc.UnsavedRefresh))
        {
            return;
        }

        await OpenCoreAsync(key, confirmDirty: false);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentKey is not { } key || !CanSaveString)
        {
            return;
        }

        try
        {
            if (!IsTruncated)
            {
                var latest = await _workspace.Session.Values.GetStringAsync(key, PreviewLimit, MaxFullLoad);
                if (!latest.IsTruncated && !latest.Value.AsSpan().SequenceEqual(_original))
                {
                    if (!await _workspace.Owner.Prompt.ConfirmAsync(Loc.Save, Loc.OverwriteConfirm))
                    {
                        return;
                    }
                }
            }

            var bytes = ValueViewerPipeline.Encode(ValueText, SelectedViewer);
            TimeSpan? ttl = int.TryParse(TtlInput, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
            await _workspace.Session.Values.SaveStringAsync(key, bytes, ttl);
            _workspace.Owner.Prompt.Info(Loc.Saved);
            _originalText = ValueText;
            await OpenCoreAsync(key, confirmDirty: false);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("保存失败", "Save failed"), ex);
        }
    }

    [RelayCommand]
    private async Task ApplyTtlAsync()
    {
        if (_currentKey is not { } key || _workspace.IsReadOnly)
        {
            return;
        }

        if (!int.TryParse(TtlInput.Trim(), out var seconds))
        {
            return;
        }

        if (seconds == 0)
        {
            if (!await _workspace.Owner.Prompt.ConfirmAsync(Loc.DeleteKey, Loc.TtlDeleteConfirm))
            {
                return;
            }

            await DeleteAsync();
            return;
        }

        try
        {
            TimeSpan? ttl = seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
            await _workspace.Session.Values.SetTtlAsync(key, ttl);
            _workspace.Owner.Prompt.Info(Loc.TtlUpdated);
            await OpenCoreAsync(key, confirmDirty: false);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("设置 TTL 失败", "Failed to set TTL"), ex);
        }
    }

    [RelayCommand]
    private Task PersistTtlAsync()
    {
        TtlInput = "-1";
        return ApplyTtlAsync();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_currentKey is not { } key || string.IsNullOrWhiteSpace(RenameTo) || _workspace.IsReadOnly)
        {
            return;
        }

        var trimmed = RenameTo.Trim();
        if (string.Equals(trimmed, KeyName, StringComparison.Ordinal))
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync(
                Loc.Rename,
                Loc.T($"确定将「{KeyName}」重命名为「{trimmed}」？", $"Rename '{KeyName}' to '{trimmed}'?")))
        {
            return;
        }

        try
        {
            var newKey = RedisKeyBytes.FromUtf8(trimmed);
            await _workspace.Session.Values.RenameAsync(key, newKey);
            _originalText = ValueText;
            await OpenCoreAsync(newKey, confirmDirty: false);
            _workspace.Browser.RemoveScannedKey(key);
            _workspace.Browser.AddScannedKey(newKey);
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("重命名失败", "Rename failed"), ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_currentKey is not { } key || _workspace.IsReadOnly)
        {
            return;
        }

        if (!await _workspace.Owner.Prompt.ConfirmAsync(Loc.DeleteKey, Loc.T($"确定删除「{KeyName}」？", $"Delete '{KeyName}'?")))
        {
            return;
        }

        try
        {
            await _workspace.Session.Values.DeleteAsync(key);
            _workspace.Owner.Prompt.Info(Loc.Deleted);
            _workspace.Browser.RemoveScannedKey(key);
            AutoRefresh = false;
            Clear();
        }
        catch (Exception ex)
        {
            await _workspace.Owner.Prompt.ErrorAsync(Loc.T("删除失败", "Delete failed"), ex);
        }
    }

    [RelayCommand]
    private async Task CopyValueAsync()
    {
        await CopyTextAsync(ValueText);
    }

    [RelayCommand]
    private async Task DumpAsync()
    {
        if (_currentKey is not { } key)
        {
            return;
        }

        if (!IsString)
        {
            _workspace.Owner.Prompt.Info(Loc.T("集合类型请在行内导出", "Dump collection rows from the table."));
            return;
        }

        var command = $"SET {ValueViewerPipeline.QuoteRedisArg(key.Value)} {ValueViewerPipeline.QuoteRedisArg(_original)}";
        await CopyTextAsync(command);
    }

    partial void OnSelectedViewerOptionChanged(ViewerOption? value)
    {
        if (_suppressViewer || value is null || value.Kind == SelectedViewer)
        {
            return;
        }

        _ = SwitchViewerAsync(value.Kind);
    }

    partial void OnAutoRefreshChanged(bool value)
    {
        _autoRefreshTimer.Stop();
        if (value)
        {
            _autoRefreshTimer.Start();
            _ = RefreshAsync();
        }
    }

    private async Task SwitchViewerAsync(ValueViewKind kind)
    {
        if (IsDirty && !await _workspace.Owner.Prompt.ConfirmAsync(Loc.Unsaved, Loc.UnsavedSwitchViewer))
        {
            RestoreViewerOption(SelectedViewer);
            return;
        }

        ApplyViewer(kind);
    }

    private async Task AutoRefreshTickAsync()
    {
        if (!AutoRefresh || IsBusy || IsDirty || _currentKey is null)
        {
            return;
        }

        await OpenCoreAsync(_currentKey.Value, confirmDirty: false);
    }

    private void ApplyViewer(ValueViewKind kind)
    {
        var result = ValueViewerPipeline.Decode(_original, kind);
        SelectedViewer = result.Kind;
        RestoreViewerOption(result.Kind == ValueViewKind.OverSize ? ValueViewKind.Text : result.Kind);
        ValueText = result.Text;
        _originalText = ValueText;
        _canEncode = result.CanEncode && !IsTruncated;
        ViewerWarning = result.Warning ?? string.Empty;
        OnPropertyChanged(nameof(ShowViewerWarning));
        OnPropertyChanged(nameof(CanSaveString));
        OnPropertyChanged(nameof(CanChangeViewer));
        OnPropertyChanged(nameof(IsDirty));
    }

    private void RestoreViewerOption(ValueViewKind kind)
    {
        _suppressViewer = true;
        SelectedViewerOption = ViewerOptions.FirstOrDefault(item => item.Kind == kind) ?? ViewerOptions[0];
        _suppressViewer = false;
    }

    private void ApplyTtlCountdown(TimeSpan? ttl)
    {
        if (ttl is { } value && value > TimeSpan.Zero)
        {
            _ttlEnd = DateTimeOffset.Now + value;
            _ttlTimer.Start();
            TickTtl();
            return;
        }

        _ttlEnd = null;
        _ttlTimer.Stop();
        TtlText = Loc.Persistent;
    }

    public void Clear()
    {
        _currentKey = null;
        HasKey = false;
        KeyName = string.Empty;
        TypeName = string.Empty;
        ValueText = string.Empty;
        IsString = false;
        IsCollection = false;
        IsStream = false;
        AutoRefresh = false;
        _ttlTimer.Stop();
        _autoRefreshTimer.Stop();
        _ttlEnd = null;
        Collection.Clear();
        Stream.Clear();
        NotifyEditorState();
    }

    private void TickTtl()
    {
        if (_ttlEnd is not { } end)
        {
            return;
        }

        var remain = end - DateTimeOffset.Now;
        if (remain <= TimeSpan.Zero)
        {
            _ttlTimer.Stop();
            TtlText = Loc.T("已过期", "Expired");
            return;
        }

        TtlText = remain.TotalHours >= 1
            ? Loc.T($"{(int)remain.TotalHours} 小时 {remain.Minutes} 分", $"{(int)remain.TotalHours}h {remain.Minutes}m")
            : Loc.T($"{(int)remain.TotalSeconds} 秒", $"{(int)remain.TotalSeconds}s");
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var top = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (top?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            _workspace.Owner.Prompt.Info(Loc.Copied);
        }
    }

    private void NotifyEditorState()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanSaveString));
        OnPropertyChanged(nameof(CanChangeViewer));
        OnPropertyChanged(nameof(ShowUnsupported));
        OnPropertyChanged(nameof(ShowViewerWarning));
        OnPropertyChanged(nameof(IsDirty));
    }
}
