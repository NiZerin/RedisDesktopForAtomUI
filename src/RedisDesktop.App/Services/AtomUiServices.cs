using AtomUI;
using AtomUI.Data;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using AtomUI.Theme.Resources;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using RedisDesktop.App.ViewModels;
using RedisDesktop.App.Views;
using RedisDesktop.Core;
using AtomButton = AtomUI.Desktop.Controls.Button;
using AtomScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using AtomTextBlock = AtomUI.Desktop.Controls.TextBlock;
using AtomWindow = AtomUI.Desktop.Controls.Window;

namespace RedisDesktop.App;

public sealed class AtomUiThemeService : IThemeService
{
    private string _mode = "light";
    private bool _watchingSystem;
    private int _applyVersion;

    public void Apply(AppSettings settings)
        => ApplyTheme(settings.ThemeMode, settings.IsDarkTheme);

    public void ApplyTheme(string themeMode, bool isDark)
    {
        _mode = NormalizeMode(themeMode, isDark);
        EnsureSystemWatcher();
        _ = ApplyCoreAsync(_mode);
    }

    private async Task ApplyCoreAsync(string mode)
    {
        var version = Interlocked.Increment(ref _applyVersion);
        var manager = Application.Current?.GetThemeManager();
        if (manager is null)
        {
            return;
        }

        var dark = IsDark(mode);
        var config = new ThemeConfigBuilder()
            .WithAlgorithms(dark
                ? [ThemeAlgorithm.Default, ThemeAlgorithm.Dark]
                : [ThemeAlgorithm.Default])
            .Build();
        var reason = string.Equals(mode, "system", StringComparison.OrdinalIgnoreCase)
            ? ThemeTransitionReason.FollowSystem
            : ThemeTransitionReason.UserRequest;
        await manager.ApplyThemeAsync(new ThemeRequest(IThemeManager.DEFAULT_THEME_ID, config, reason));
        if (version != _applyVersion)
        {
            return;
        }
    }

    private void EnsureSystemWatcher()
    {
        if (_watchingSystem)
        {
            return;
        }

        var settings = Application.Current?.PlatformSettings;
        if (settings is null)
        {
            return;
        }

        _watchingSystem = true;
        settings.ColorValuesChanged += (_, _) =>
        {
            if (string.Equals(_mode, "system", StringComparison.OrdinalIgnoreCase))
            {
                _ = ApplyCoreAsync("system");
            }
        };
    }

    private static string NormalizeMode(string? themeMode, bool isDark)
    {
        if (!string.IsNullOrWhiteSpace(themeMode)
            && (themeMode.Equals("system", StringComparison.OrdinalIgnoreCase)
                || themeMode.Equals("light", StringComparison.OrdinalIgnoreCase)
                || themeMode.Equals("dark", StringComparison.OrdinalIgnoreCase)))
        {
            return themeMode.ToLowerInvariant();
        }

        return isDark ? "dark" : "system";
    }

    private static bool IsDark(string mode)
    {
        if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var app = Application.Current;
        if (app?.PlatformSettings is { } settings)
        {
            return settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        }

        return app?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
    }
}

public sealed class AtomUiUserPrompt : IUserPrompt
{
    public void Info(string message)
    {
        if (GetMainWindow()?.DataContext is MainWindowViewModel vm)
        {
            vm.StatusText = message;
        }
    }

    public Task ErrorAsync(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message}\n{exception.Message}";
        return ShowMessageAsync("错误", text, showCancel: false);
    }

    public Task<bool> ConfirmAsync(string title, string message)
        => ShowMessageAsync(title, message, showCancel: true);

    public Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel viewModel)
        => ShowConnectionEditorWindowAsync(viewModel);

    public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
        => ShowSettingsWindowAsync(viewModel);

    public Task ShowHotkeysAsync(SettingsViewModel viewModel)
        => ShowContentAsync(viewModel.Loc.Hotkey, new HotkeysView { DataContext = viewModel }, 560, 480, showCancel: false, loc: viewModel.Loc);

    public Task ShowCommandLogAsync(CommandLogViewModel viewModel)
        => ShowCommandLogWindowAsync(viewModel);

    public Task<bool> ShowNewKeyDialogAsync(NewKeyDialogViewModel viewModel)
        => ShowNewKeyWindowAsync(viewModel);

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PromptTextAsync(string title, string message, string? initial = null)
    {
        var vm = new PromptTextViewModel
        {
            Message = message,
            Text = initial ?? string.Empty
        };
        var ok = await ShowContentAsync(title, new PromptTextView { DataContext = vm }, 440, 220);
        return ok ? vm.Text : null;
    }

    public async Task<AppUpdatePromptResult> ShowUpdateAvailableAsync(UpdateAvailableViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return AppUpdatePromptResult.Cancel;
        }

        var result = await Dialog.ShowDialogModalAsync(
            new UpdateAvailableView { DataContext = viewModel },
            viewModel,
            new DialogOptions
            {
                Title = viewModel.Loc.UpdateAvailable,
                IsFooterVisible = false,
                IsClosable = true,
                IsDragMovable = true,
                IsMaximizable = false,
                IsMinimizable = false,
                DialogHostType = DialogHostType.Overlay,
                HostMinWidth = 460,
                HostMaxWidth = 620
            },
            owner);
        return result is AppUpdatePromptResult choice ? choice : AppUpdatePromptResult.Cancel;
    }

    public async Task ShowUpdateDownloadAsync(UpdateDownloadViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return;
        }

        await Dialog.ShowDialogModalAsync(
            new UpdateDownloadView { DataContext = viewModel },
            viewModel,
            new DialogOptions
            {
                Title = viewModel.Loc.DownloadingUpdate,
                IsFooterVisible = false,
                IsClosable = false,
                IsDragMovable = true,
                IsMaximizable = false,
                IsMinimizable = false,
                DialogHostType = DialogHostType.Overlay,
                HostMinWidth = 440,
                HostMaxWidth = 560
            },
            owner);
    }

    public async Task ShowUpdateRestartNoticeAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return;
        }

        await MessageBox.ShowMessageBoxModalAsync(
            new AtomTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            null,
            new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Information,
                HostType = DialogHostType.Overlay,
                IsDragMovable = true,
                MinWidth = 420,
                MaxWidth = 560,
                OkButtonText = owner.DataContext is MainWindowViewModel vm ? vm.Loc.Ok : "OK"
            },
            owner);
    }

    private static Avalonia.Controls.Window? GetMainWindow()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    private static void ApplyWindowSurfaces(AtomWindow window)
    {
        TokenResourceBinder.CreateGlobalTokenBinding(window, AtomWindow.BackgroundProperty, SharedTokenKind.ColorBgLayout);
        TokenResourceBinder.CreateGlobalTokenBinding(window, AtomWindow.ContentFrameBackgroundProperty, SharedTokenKind.ColorBgLayout);
        TokenResourceBinder.CreateGlobalTokenBinding(window, AtomWindow.TitleBarFrameBackgroundProperty, SharedTokenKind.ColorBgContainer);
        TokenResourceBinder.CreateGlobalTokenBinding(window, AtomWindow.TransparencyBackgroundFallbackProperty, SharedTokenKind.ColorBgLayout);
        TokenResourceBinder.CreateGlobalTokenBinding(window, AtomWindow.ForegroundProperty, SharedTokenKind.ColorText);
        window.Icon = AppAssets.WindowIcon;
    }

    private static Task<bool> ShowMessageAsync(string title, string message, bool showCancel)
        => ShowContentAsync(
            title,
            new AtomTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            420,
            220,
            showCancel);

    private static async Task<bool> ShowConnectionEditorWindowAsync(ConnectionEditorViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return false;
        }

        var loc = viewModel.Loc;
        var confirmed = false;
        var test = new AtomButton
        {
            Content = loc.TestConnection,
            Command = viewModel.TestConnectionCommand
        };
        var cancel = new AtomButton { Content = loc.Cancel, MinWidth = 80 };
        var ok = new AtomButton
        {
            Content = loc.Ok,
            ButtonType = ButtonType.Primary,
            MinWidth = 80
        };
        var window = new AtomWindow
        {
            Title = viewModel.IsNew ? loc.NewConnection : loc.EditConnection,
            Width = 860,
            MinWidth = 640,
            MinHeight = 360,
            MaxHeight = 820,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };
        ApplyWindowSurfaces(window);

        ok.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(test);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new DockPanel { Margin = new Thickness(20, 16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new AtomScrollViewer
        {
            Content = new ConnectionEditorView { DataContext = viewModel }
        });
        window.Content = root;

        await window.ShowDialog(owner);
        return confirmed;
    }

    private static async Task<bool> ShowNewKeyWindowAsync(NewKeyDialogViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return false;
        }

        var loc = viewModel.Loc;
        var confirmed = false;
        var cancel = new AtomButton { Content = loc.Cancel, MinWidth = 80 };
        var ok = new AtomButton
        {
            Content = loc.Ok,
            ButtonType = ButtonType.Primary,
            MinWidth = 80
        };
        var window = new AtomWindow
        {
            Title = loc.NewKey,
            Width = 420,
            MinWidth = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };
        ApplyWindowSurfaces(window);

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(viewModel.KeyName))
            {
                return;
            }

            confirmed = true;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new DockPanel { Margin = new Thickness(16), ClipToBounds = false };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new NewKeyDialogView { DataContext = viewModel });
        window.Content = root;

        await window.ShowDialog(owner);
        return confirmed;
    }

    private static async Task<bool> ShowSettingsWindowAsync(SettingsViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return false;
        }

        var loc = viewModel.Loc;
        var confirmed = false;
        var cancel = new AtomButton { Content = loc.Cancel, MinWidth = 80 };
        var ok = new AtomButton
        {
            Content = loc.Ok,
            ButtonType = ButtonType.Primary,
            MinWidth = 80
        };
        var window = new AtomWindow
        {
            Title = loc.Settings,
            Width = 780,
            MinWidth = 640,
            Height = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };
        ApplyWindowSurfaces(window);

        ok.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new AtomScrollViewer
        {
            Content = new SettingsView { DataContext = viewModel }
        });
        window.Content = root;

        await window.ShowDialog(owner);
        return confirmed;
    }

    private static async Task ShowCommandLogWindowAsync(CommandLogViewModel viewModel)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return;
        }

        var loc = viewModel.Loc;
        var close = new AtomButton { Content = loc.Cancel, MinWidth = 80 };
        var clear = new AtomButton { Content = loc.ClearLogs, MinWidth = 80 };
        var window = new AtomWindow
        {
            Title = loc.CommandLog,
            Width = Math.Max(720, owner.Width * 0.9),
            Height = Math.Max(420, owner.Height * 0.7),
            MinWidth = 640,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };
        ApplyWindowSurfaces(window);

        close.Click += (_, _) => window.Close();
        clear.Click += (_, _) => viewModel.ClearCommand.Execute(null);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        right.Children.Add(close);
        DockPanel.SetDock(clear, Dock.Left);
        buttons.Children.Add(clear);
        buttons.Children.Add(right);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new CommandLogView { DataContext = viewModel });
        window.Content = root;

        await window.ShowDialog(owner);
    }

    private static async Task<bool> ShowContentAsync(
        string title,
        Control content,
        double width,
        double height,
        bool showCancel = true,
        UiStrings? loc = null)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return false;
        }

        var confirmed = false;
        var okText = loc?.Ok ?? "确定";
        var cancelText = loc?.Cancel ?? "取消";
        var ok = new AtomButton
        {
            Content = okText,
            ButtonType = ButtonType.Primary,
            MinWidth = 80
        };
        var cancel = new AtomButton { Content = cancelText, MinWidth = 80, IsVisible = showCancel };
        var window = new AtomWindow
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(360, width),
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };
        ApplyWindowSurfaces(window);

        ok.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new AtomScrollViewer { Content = content });
        window.Content = root;

        await window.ShowDialog(owner);
        return confirmed;
    }
}
