using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly IAppUpdateService _updates;
    private readonly IBenchmarkService _benchmark;

    public MainWindowViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore settingsStore,
        ISecretProtector secretProtector,
        IRedisSessionFactory sessionFactory,
        IUserPrompt prompt,
        IThemeService themeService,
        ICommandLog commandLog,
        IAppUpdateService updates,
        IBenchmarkService benchmark)
    {
        _connectionStore = connectionStore;
        _settingsStore = settingsStore;
        _secretProtector = secretProtector;
        _sessionFactory = sessionFactory;
        _prompt = prompt;
        _themeService = themeService;
        _updates = updates;
        _benchmark = benchmark;
        Aside = new AsideViewModel(this);
        Workspace = new WorkspaceViewModel(this);
        CommandLog = new CommandLogViewModel(commandLog, this);
        Loc = new UiStrings();
        StatusText = Loc.T("正在加载…", "Loading…");
    }

    public UiStrings Loc { get; }

    public AsideViewModel Aside { get; }

    public WorkspaceViewModel Workspace { get; }

    public CommandLogViewModel CommandLog { get; }

    public AppSettings Settings { get; private set; } = new();

    [ObservableProperty]
    private double _sideBarWidth = 280;

    [ObservableProperty]
    private string _statusText = "正在加载…";

    [ObservableProperty]
    private double _zoomFactor = 1;

    [ObservableProperty]
    private FontFamily? _uiFontFamily;

    [ObservableProperty]
    private bool _isInitializing = true;

    public ISecretProtector Secrets => _secretProtector;

    public IRedisSessionFactory Sessions => _sessionFactory;

    public IUserPrompt Prompt => _prompt;

    public IAppUpdateService Updates => _updates;

    public IBenchmarkService Benchmark => _benchmark;

    public int ScanCount => Math.Clamp(Settings.ScanCount <= 0 ? 200 : Settings.ScanCount, 10, 20_000);

    private int _initializeStarted;
    private bool _settingsLoaded;

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _initializeStarted, 1) == 1)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            Settings = await _settingsStore.LoadAsync();
            ApplyLoadedSettings();
            _settingsLoaded = true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var connections = await _connectionStore.LoadAsync();
            Aside.Load(connections);
            StatusText = connections.Count == 0
                ? Loc.T("新建一个连接以开始", "Create a connection to start")
                : Loc.T($"已加载 {connections.Count} 个连接", $"Loaded {connections.Count} connections");
            FinishPendingUpdateIfCurrent();
            _ = CheckForUpdatesOnStartupAsync();
            _ = Task.Run(HealInstallCopies);
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup", ex);
            StatusText = Loc.T($"启动失败：{ex.Message}", $"Startup failed: {ex.Message}");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private void ApplyLoadedSettings()
    {
        SideBarWidth = Settings.SideBarWidth < 220 ? 280 : Settings.SideBarWidth;
        if (string.IsNullOrWhiteSpace(Settings.ThemeMode))
        {
            Settings.ThemeMode = Settings.IsDarkTheme ? "dark" : "system";
        }

        Settings.Language = UiLanguages.Normalize(Settings.Language);
        Loc.SetLanguage(Settings.Language);
        UiLanguages.ApplyAtomUi(Settings.Language);
        _themeService.Apply(Settings);
        ApplyAppearance(Settings);
    }

    private static void HealInstallCopies()
    {
        try
        {
            AppUpdateApplier.ReplaceOutdatedInstallCopies();
        }
        catch (Exception ex)
        {
            AppLog.Error("UpdateHeal", ex);
        }
    }

    public async Task SaveConnectionsAsync()
    {
        await _connectionStore.SaveAsync(Aside.GetConfigs());
    }

    public void PersistSettings()
    {
        if (!_settingsLoaded)
        {
            return;
        }

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
        if (!_settingsLoaded)
        {
            return;
        }

        Settings.SideBarWidth = SideBarWidth < 220 ? 280 : SideBarWidth;
        await _settingsStore.SaveAsync(Settings);
    }

    public void Shutdown()
    {
        Aside.StopAllHeartbeats();
        if (!_settingsLoaded)
        {
            return;
        }

        PersistSettings();
        _updates.TryApplyPending(Settings);
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
        Settings.Language = UiLanguages.Normalize(Settings.Language);
        Loc.SetLanguage(Settings.Language);
        UiLanguages.ApplyAtomUi(Settings.Language);
        _themeService.Apply(Settings);
        ApplyAppearance(Settings);
        await PersistSettingsAsync();
    }

    public void ApplyTheme(string themeMode, bool isDark) => _themeService.ApplyTheme(themeMode, isDark);

    public void PreviewZoom(double zoom)
    {
        ZoomFactor = Math.Clamp(zoom <= 0 ? 1 : zoom, 0.5, 2.0);
    }

    public void PreviewFont(string? fontName, string? language = null)
    {
        UiFontFamily = UiFontCatalog.Create(
            string.IsNullOrWhiteSpace(fontName) ? [] : [fontName],
            language ?? Settings.Language);
    }

    public void ApplyAppearance(AppSettings settings)
    {
        PreviewZoom(settings.ZoomFactor);
        var fonts = (settings.FontFamilies ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim());
        UiFontFamily = UiFontCatalog.Create(fonts, settings.Language);
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
        Settings = Settings.ResetUserData(SideBarWidth);
        Loc.SetLanguage(Settings.Language);
        UiLanguages.ApplyAtomUi(Settings.Language);
        _themeService.Apply(Settings);
        ApplyAppearance(Settings);
        await PersistSettingsAsync();
        StatusText = Loc.ClearCacheDone;
        return true;
    }

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

    public Task OpenSlowLogAsync(ConnectionItemViewModel item)
        => OpenConnectedToolAsync(item, connection => Workspace.OpenSlowLogAsync(connection));

    public Task OpenMemoryAnalysisAsync(ConnectionItemViewModel item, string? prefix = null)
        => OpenConnectedToolAsync(item, connection => Workspace.OpenMemoryAnalysisAsync(connection, prefix));

    public Task OpenBenchmarkAsync(ConnectionItemViewModel item)
        => OpenConnectedToolAsync(item, connection => Workspace.OpenBenchmarkAsync(connection));

    private async Task OpenConnectedToolAsync(ConnectionItemViewModel item, Func<ConnectionItemViewModel, Task> open)
    {
        if (item.State != SessionState.Connected || item.Session is null)
        {
            await ConnectAsync(item);
        }

        if (item.Session is not null)
        {
            await open(item);
        }
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

    public async Task<AppReleaseInfo?> CheckLatestReleaseAsync()
    {
        try
        {
            return await _updates.GetLatestAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task PromptAndInstallUpdateAsync(AppReleaseInfo release, bool fromStartup)
    {
        if (fromStartup)
        {
            var choice = await _prompt.ShowUpdateAvailableAsync(new UpdateAvailableViewModel(Loc, release, _updates.CurrentVersion));
            if (choice == AppUpdatePromptResult.Mute)
            {
                Settings.MuteUpdatePrompt = true;
                await PersistSettingsAsync();
                StatusText = Loc.T("已关闭启动更新提示，可在设置中手动更新。", "Startup update prompts are off. You can still update from Settings.");
                return;
            }

            if (choice != AppUpdatePromptResult.Update)
            {
                return;
            }
        }

        await _prompt.ShowUpdateRestartNoticeAsync(
            Loc.UpdateAvailable,
            Loc.T(
                $"软件将下载 {release.Version}，并在重启后自动替换当前应用。",
                $"The app will download {release.Version} and replace itself after restart."));
        await DownloadPendingUpdateAsync(release);
    }

    private async Task DownloadPendingUpdateAsync(AppReleaseInfo release)
    {
        var download = new UpdateDownloadViewModel(Loc, release);
        using var cts = new CancellationTokenSource();
        download.CancelRequested += () => cts.Cancel();
        var dialog = _prompt.ShowUpdateDownloadAsync(download);
        try
        {
            var progress = new Progress<AppUpdateProgress>(sample =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => download.Report(sample));
            });
            var path = await _updates.DownloadAndExtractAsync(release, progress, cts.Token);
            Settings.PendingUpdateVersion = release.Version;
            Settings.PendingUpdatePackagePath = path;
            Settings.PendingUpdateTargetExe = Environment.ProcessPath;
            await PersistSettingsAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(download.Complete);
            await dialog;
            StatusText = _updates.CanReplaceRunningApp
                ? Loc.T("更新已就绪，关闭程序后将自动替换并启动新版本。", "Update is ready. Close the app to replace files and relaunch.")
                : Loc.T("更新包已下载。当前是开发运行，关闭后不会自动替换。", "Update package downloaded. Dev hosts cannot replace the running process.");
        }
        catch (OperationCanceledException)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => download.Fail(Loc.T("已取消下载", "Download cancelled")));
            await dialog;
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => download.Fail(ex.Message));
            await dialog;
            await _prompt.ErrorAsync(Loc.T("下载更新失败", "Failed to download the update"), ex);
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (Settings.MuteUpdatePrompt)
        {
            return;
        }

        var latest = await CheckLatestReleaseAsync();
        if (latest is null || !AppReleaseParser.IsNewer(latest.Version, _updates.CurrentVersion))
        {
            return;
        }

        await PromptAndInstallUpdateAsync(latest, fromStartup: true);
    }

    private void FinishPendingUpdateIfCurrent()
    {
        if (string.IsNullOrWhiteSpace(Settings.PendingUpdateVersion))
        {
            return;
        }

        if (AppReleaseParser.IsNewer(Settings.PendingUpdateVersion, _updates.CurrentVersion))
        {
            return;
        }

        AppUpdateApplier.ReplaceOutdatedInstallCopies();
        _updates.ClearPending(Settings);
        PersistSettings();
    }
}
