using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionStore _connectionStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISecretProtector _secretProtector;
    private readonly IRedisSessionFactory _sessionFactory;
    private readonly IUserPrompt _prompt;
    private readonly IThemeService _themeService;

    public MainWindowViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore settingsStore,
        ISecretProtector secretProtector,
        IRedisSessionFactory sessionFactory,
        IUserPrompt prompt,
        IThemeService themeService,
        ICommandLog commandLog)
    {
        _connectionStore = connectionStore;
        _settingsStore = settingsStore;
        _secretProtector = secretProtector;
        _sessionFactory = sessionFactory;
        _prompt = prompt;
        _themeService = themeService;
        Aside = new AsideViewModel(this);
        Workspace = new WorkspaceViewModel(this);
        CommandLog = new CommandLogViewModel(commandLog, this);
        Loc = new UiStrings();
    }

    public UiStrings Loc { get; }

    public AsideViewModel Aside { get; }

    public WorkspaceViewModel Workspace { get; }

    public CommandLogViewModel CommandLog { get; }

    public AppSettings Settings { get; private set; } = new();

    [ObservableProperty]
    private double _sideBarWidth = 280;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private double _zoomFactor = 1;

    [ObservableProperty]
    private FontFamily? _uiFontFamily;

    public ISecretProtector Secrets => _secretProtector;

    public IRedisSessionFactory Sessions => _sessionFactory;

    public IUserPrompt Prompt => _prompt;

    public int ScanCount => Math.Clamp(Settings.ScanCount <= 0 ? 200 : Settings.ScanCount, 10, 20_000);

    public async Task InitializeAsync()
    {
        try
        {
            Settings = await _settingsStore.LoadAsync();
            SideBarWidth = Settings.SideBarWidth < 220 ? 280 : Settings.SideBarWidth;
            if (string.IsNullOrWhiteSpace(Settings.ThemeMode))
            {
                Settings.ThemeMode = Settings.IsDarkTheme ? "dark" : "system";
            }

            Loc.SetLanguage(Settings.Language);
            _themeService.Apply(Settings);
            ApplyAppearance(Settings);
            var connections = await _connectionStore.LoadAsync();
            Aside.Load(connections);
            StatusText = connections.Count == 0
                ? Loc.T("新建一个连接以开始", "Create a connection to start")
                : Loc.T($"已加载 {connections.Count} 个连接", $"Loaded {connections.Count} connections");
        }
        catch (Exception ex)
        {
            StatusText = Loc.T($"启动失败：{ex.Message}", $"Startup failed: {ex.Message}");
        }
    }

    public async Task SaveConnectionsAsync()
    {
        await _connectionStore.SaveAsync(Aside.GetConfigs());
    }

    public void PersistSettings()
    {
        Settings.SideBarWidth = SideBarWidth < 220 ? 280 : SideBarWidth;
        try
        {
            _settingsStore.Save(Settings);
        }
        catch
        {
            // shutdown path: never block or throw
        }
    }

    public async Task PersistSettingsAsync()
    {
        Settings.SideBarWidth = SideBarWidth < 220 ? 280 : SideBarWidth;
        await _settingsStore.SaveAsync(Settings);
    }

    public void Shutdown()
    {
        Aside.StopAllHeartbeats();
        PersistSettings();
    }

    [RelayCommand]
    public async Task NewConnectionAsync()
    {
        var editor = ConnectionEditorViewModel.CreateNew();
        editor.Loc = Loc;
        if (!await _prompt.ShowConnectionEditorAsync(editor))
        {
            return;
        }

        if (!editor.TryBuild(out var config, out var error))
        {
            await _prompt.ErrorAsync(error ?? "参数无效");
            return;
        }

        editor.ApplyProtectedSecrets(_secretProtector, config, existing: null);
        Aside.Add(config);
        await SaveConnectionsAsync();
        StatusText = Loc.T($"已保存连接「{config.Name}」", $"Saved connection \"{config.Name}\"");
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        var vm = new SettingsViewModel(this);
        if (!await _prompt.ShowSettingsAsync(vm))
        {
            vm.RevertPreview();
            return;
        }

        vm.ApplyTo(Settings);
        Loc.SetLanguage(Settings.Language);
        _themeService.Apply(Settings);
        ApplyAppearance(Settings);
        await PersistSettingsAsync();
    }

    public void ApplyTheme(string themeMode, bool isDark) => _themeService.ApplyTheme(themeMode, isDark);

    public void PreviewZoom(double zoom)
    {
        ZoomFactor = Math.Clamp(zoom <= 0 ? 1 : zoom, 0.5, 2.0);
    }

    public void ApplyAppearance(AppSettings settings)
    {
        PreviewZoom(settings.ZoomFactor);
        var fonts = (settings.FontFamilies ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        UiFontFamily = fonts.Count == 0
            ? null
            : new FontFamily(string.Join(",", fonts.Select(QuoteFontFamily)));
    }

    public async Task<bool> ClearCacheAsync()
    {
        if (!await _prompt.ConfirmAsync(Loc.ClearCache, Loc.ClearCacheTip))
        {
            return false;
        }

        await Workspace.CloseAllAsync();
        await Aside.DisconnectAllAsync();
        Aside.Load([]);
        await _connectionStore.SaveAsync([]);
        Settings = new AppSettings { SideBarWidth = SideBarWidth };
        Loc.SetLanguage(Settings.Language);
        _themeService.Apply(Settings);
        ApplyAppearance(Settings);
        await PersistSettingsAsync();
        StatusText = Loc.ClearCacheDone;
        return true;
    }

    private static string QuoteFontFamily(string name)
        => name.Contains(' ', StringComparison.Ordinal) || name.Contains(',', StringComparison.Ordinal)
            ? $"\"{name}\""
            : name;

    [RelayCommand]
    private Task ToggleCommandLogAsync() => _prompt.ShowCommandLogAsync(CommandLog);

    [RelayCommand]
    private async Task ExportConnectionsAsync()
    {
        if (!await _prompt.ConfirmAsync(Loc.Export, Loc.ExportWarning))
        {
            return;
        }

        var path = await _prompt.PickSaveFileAsync(Loc.Export, "connections.json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, ConnectionTransfer.ToJson(Aside.GetConfigs()));
        StatusText = Loc.T("已导出", "Exported");
    }

    [RelayCommand]
    private async Task ImportConnectionsAsync()
    {
        var path = await _prompt.PickOpenFileAsync(Loc.Import);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var items = ConnectionTransfer.FromJson(json);
            foreach (var item in items)
            {
                Aside.Add(item);
            }

            await SaveConnectionsAsync();
            StatusText = Loc.T($"已导入 {items.Count} 个连接", $"Imported {items.Count} connections");
        }
        catch (Exception ex)
        {
            await _prompt.ErrorAsync(Loc.T("导入失败", "Import failed"), ex);
        }
    }

    public async Task ConnectAsync(ConnectionItemViewModel item)
    {
        if (item.State == SessionState.Connected)
        {
            item.IsExpanded = true;
            Workspace.Activate(item);
            return;
        }

        if (item.State == SessionState.Connecting)
        {
            return;
        }

        item.State = SessionState.Connecting;
        StatusText = $"正在连接 {item.Config.Name}...";
        try
        {
            var secrets = ConnectionSecrets.From(item.Config, _secretProtector);
            var session = await _sessionFactory.ConnectAsync(item.Config, secrets);
            item.AttachSession(session);
            try
            {
                item.IsExpanded = true;
                Workspace.Open(item, scanKeys: true);
            }
            catch (Exception ex)
            {
                StatusText = Loc.T($"工作区打开失败：{ex.Message}", $"Failed to open workspace: {ex.Message}");
            }
            StatusText = $"{Loc.Connected} {item.Config.Name}";
            await SaveConnectionsAsync();
        }
        catch (Exception ex)
        {
            item.State = SessionState.Failed;
            item.IsExpanded = false;
            item.StatusText = ex.Message;
            StatusText = "连接失败";
            await _prompt.ErrorAsync($"连接 {item.Config.Name} 失败", ex);
        }
    }

    public async Task DisconnectAsync(ConnectionItemViewModel item)
    {
        await Workspace.CloseAsync(item);
        await item.DetachSessionAsync();
        StatusText = $"已断开 {item.Config.Name}";
    }

    public Task OpenCliAsync(ConnectionItemViewModel item)
    {
        if (item.State != SessionState.Connected || item.Session is null)
        {
            return ConnectThenCliAsync(item);
        }

        return Workspace.OpenCliAsync(item);
    }

    public Task OpenPubSubAsync(ConnectionItemViewModel item)
    {
        if (item.State != SessionState.Connected || item.Session is null)
        {
            return ConnectThenPubSubAsync(item);
        }

        return Workspace.OpenPubSubAsync(item);
    }

    private async Task ConnectThenPubSubAsync(ConnectionItemViewModel item)
    {
        await ConnectAsync(item);
        if (item.Session is not null)
        {
            await Workspace.OpenPubSubAsync(item);
        }
    }

    private async Task ConnectThenCliAsync(ConnectionItemViewModel item)
    {
        await ConnectAsync(item);
        if (item.Session is not null)
        {
            await Workspace.OpenCliAsync(item);
        }
    }

    public async Task EditConnectionAsync(ConnectionItemViewModel item)
    {
        var editor = ConnectionEditorViewModel.FromConfig(item.Config, _secretProtector);
        editor.Loc = Loc;
        if (!await _prompt.ShowConnectionEditorAsync(editor))
        {
            return;
        }

        if (!editor.TryBuild(out var config, out var error))
        {
            await _prompt.ErrorAsync(error ?? "参数无效");
            return;
        }

        config.Id = item.Config.Id;
        editor.ApplyProtectedSecrets(_secretProtector, config, item.Config);
        item.ReplaceConfig(config);
        Aside.Rebuild();
        await SaveConnectionsAsync();
        StatusText = Loc.T("连接已更新", "Connection updated");
    }

    public async Task SwitchDatabaseAsync(DatabaseNodeViewModel node)
    {
        var item = node.Connection;
        if (item.State != SessionState.Connected || item.Session is null)
        {
            await ConnectAsync(item);
        }

        if (item.Session is null)
        {
            return;
        }

        item.Session.SelectDatabase(node.Index);
        item.MarkActiveDatabase(node.Index);
        item.NotifyEndpointChanged();
        item.StatusHost?.Browser.ResetSearchCriteria();
        await SaveConnectionsAsync();
        Workspace.Open(item, scanKeys: true);
        StatusText = $"{Loc.Connected} {item.Config.Name} / db{node.Index}";
    }
}
